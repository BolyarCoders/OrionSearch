import logging
from threading import Lock
from uuid import uuid4

from fastapi import BackgroundTasks, Depends, FastAPI, HTTPException

from api.models import JobCreated, JobStatus, ResearchRequest, ResearchResult
from api.security import require_api_key
from api.service import run_research

app = FastAPI(title="OrionSearch Research Worker API", version="0.1.0")
logger = logging.getLogger("orionsearch.api")
jobs = {}
jobs_lock = Lock()
MAX_JOBS = 100


@app.get("/health")
def health():
    return {"status": "ok", "service": "research-worker"}


@app.post("/research", response_model=ResearchResult, dependencies=[Depends(require_api_key)])
def research(request: ResearchRequest):
    try:
        return run_research(request)
    except Exception:
        logger.exception("Research request failed")
        raise HTTPException(status_code=502, detail="Research failed. Check worker logs")


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
            jobs[job_id].update(status="failed", error="Research failed. Check worker logs")


@app.post("/research/jobs", response_model=JobCreated, status_code=202, dependencies=[Depends(require_api_key)])
def create_job(request: ResearchRequest, tasks: BackgroundTasks):
    with jobs_lock:
        if len(jobs) >= MAX_JOBS:
            raise HTTPException(status_code=503, detail="Job store is full. Restart or enable persistent storage")
        job_id = str(uuid4())
        jobs[job_id] = {"job_id": job_id, "status": "queued", "result": None, "error": None}
    tasks.add_task(process_job, job_id, request)
    return JobCreated(job_id=job_id, status="queued", status_url=f"/research/jobs/{job_id}")


@app.get("/research/jobs/{job_id}", response_model=JobStatus, dependencies=[Depends(require_api_key)])
def get_job(job_id: str):
    with jobs_lock:
        job = jobs.get(job_id)
        if job is None:
            raise HTTPException(status_code=404, detail="Job not found")
        return JobStatus(**job)
