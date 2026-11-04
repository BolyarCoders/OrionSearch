import logging
import json
import os
from threading import Lock
from uuid import uuid4

from fastapi import BackgroundTasks, Depends, FastAPI, HTTPException, Request, Response
from starlette.concurrency import run_in_threadpool

from api.models import JobCreated, JobStatus, ResearchRequest, ResearchResult
from api.security import require_api_key, require_bearer, prototype_enabled
from api.service import run_research, run_contract_research, ResearchBusy
from api.contract import MAX_REQUEST_BYTES, ContractError, validate_request, validate_result

app = FastAPI(title="OrionSearch Research Worker API", version="0.1.0")
logger = logging.getLogger("orionsearch.api")
jobs = {}
jobs_lock = Lock()
MAX_JOBS = 100


@app.get("/health")
def health():
    return {"status": "ok", "service": "research-worker"}


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON field")
        result[key] = value
    return result


def reject_constant(value):
    raise ValueError("Non-JSON numeric constant")


@app.post("/research", dependencies=[Depends(require_bearer)])
async def research(request: Request):
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "application/json":
        raise HTTPException(status_code=415, detail="JSON required")
    body = bytearray()
    async for chunk in request.stream():
        if len(body) + len(chunk) > MAX_REQUEST_BYTES:
            raise HTTPException(status_code=413, detail="Request too large")
        body.extend(chunk)
    try:
        payload = validate_request(json.loads(body.decode("utf-8"), object_pairs_hook=unique_object, parse_constant=reject_constant),
                                   allow_provisional=os.getenv("ORION_ALLOW_PROVISIONAL_CONTRACT", "").lower() == "true")
    except (ValueError, TypeError, RecursionError):
        raise HTTPException(status_code=400, detail="Invalid research request") from None
    try:
        result = await run_in_threadpool(run_contract_research, payload)
        encoded = validate_result(result, payload)
    except ResearchBusy:
        raise HTTPException(status_code=503, detail="Research service is busy", headers={"Retry-After": "10"}) from None
    except ContractError:
        raise HTTPException(status_code=422, detail="Research request or result is unsupported") from None
    except Exception:
        logger.warning("Research execution failed for run %s", payload["researchRunId"])
        raise HTTPException(status_code=503, detail="Research service is temporarily unavailable") from None
    return Response(content=encoded, media_type="application/json", headers={"Cache-Control": "no-store"})


@app.post("/prototype/research", response_model=ResearchResult, dependencies=[Depends(prototype_enabled), Depends(require_api_key)])
def prototype_research(request: ResearchRequest):
    try:
        return run_research(request)
    except Exception:
        logger.exception("Research request failed")
        raise HTTPException(
            status_code=502, detail="Research failed. Check worker logs"
        )


def process_job(job_id: str, request: ResearchRequest):
    with jobs_lock:
        jobs[job_id]["status"] = "running"
    try:
        result = run_research(request)
        with jobs_lock:
            jobs[job_id].update(status="completed", result=result)
    except Exception:
        logger.exception("Research job %s failed", job_id)
        with jobs_lock:
            jobs[job_id].update(
                status="failed", error="Research failed. Check worker logs"
            )


def _make_job_room():
    """Evict oldest terminal jobs when capacity is needed; never evict active jobs."""
    terminal_states = {"completed", "failed"}
    while len(jobs) >= MAX_JOBS:
        terminal_id = next(
            (
                job_id
                for job_id, job in jobs.items()
                if job.get("status") in terminal_states
            ),
            None,
        )
        if terminal_id is None:
            return False
        del jobs[terminal_id]
    return True


@app.post("/prototype/research/jobs", response_model=JobCreated, status_code=202, dependencies=[Depends(prototype_enabled), Depends(require_api_key)])
def create_job(request: ResearchRequest, tasks: BackgroundTasks):
    with jobs_lock:
        if not _make_job_room():
            raise HTTPException(
                status_code=503,
                detail="All job slots are occupied by queued or running jobs",
            )
        job_id = str(uuid4())
        jobs[job_id] = {
            "job_id": job_id,
            "status": "queued",
            "result": None,
            "error": None,
        }
    tasks.add_task(process_job, job_id, request)
    return JobCreated(job_id=job_id, status="queued", status_url=f"/prototype/research/jobs/{job_id}")


@app.get("/prototype/research/jobs/{job_id}", response_model=JobStatus, dependencies=[Depends(prototype_enabled), Depends(require_api_key)])
def get_job(job_id: str):
    with jobs_lock:
        job = jobs.get(job_id)
        if job is None:
            raise HTTPException(status_code=404, detail="Job not found")
        return JobStatus(**job)
