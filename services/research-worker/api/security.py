import hmac
import os

from fastapi import HTTPException, Security, Request
from fastapi.security import APIKeyHeader

api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)


def prototype_enabled():
    if os.getenv("ORION_ENABLE_PROTOTYPE_API", "").lower() != "true":
        raise HTTPException(status_code=404, detail="Not found")


def require_bearer(request: Request):
    expected = os.getenv("ORION_WORKER_API_KEY", "")
    previous = os.getenv("ORION_WORKER_PREVIOUS_API_KEY", "")
    keys = [key for key in (expected, previous) if key]
    if not expected or any(len(key) > 4096 or any(not 33 <= ord(c) <= 126 for c in key) for key in keys):
        raise HTTPException(status_code=503, detail="Research service is not configured")
    values = request.headers.getlist("authorization")
    parts = values[0].split(" ", 1) if len(values) == 1 else []
    supplied = parts[1] if len(parts) == 2 and parts[0].lower() == "bearer" else ""
    accepted = False
    for key in keys:
        accepted |= hmac.compare_digest(supplied.encode("utf-8"), key.encode("utf-8"))
    if not accepted:
        raise HTTPException(status_code=401, detail="Unauthorized", headers={"WWW-Authenticate": "Bearer"})


def require_api_key(api_key: str | None = Security(api_key_header)):
    expected = os.getenv("ORION_WORKER_API_KEY")
    if not expected:
        raise HTTPException(status_code=503, detail="Worker API key is not configured")
    if not api_key or not hmac.compare_digest(api_key, expected):
        raise HTTPException(status_code=401, detail="Invalid API key")
