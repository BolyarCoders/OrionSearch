from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field


class ResearchRequest(BaseModel):
    topic: str = Field(min_length=2, max_length=180)
    days: int = Field(default=7, ge=1, le=90)
    max_articles: int = Field(default=20, ge=1, le=50)


class ResearchResult(BaseModel):
    topic: str
    days: int
    queries: list[str]
    sources: list[str]
    total_articles: int
    articles: list[dict[str, Any]]
    report: str
    errors: dict[str, str]
    generated_at: datetime


class JobCreated(BaseModel):
    job_id: str
    status: Literal["queued"]
    status_url: str


class JobStatus(BaseModel):
    job_id: str
    status: Literal["queued", "running", "completed", "failed"]
    result: ResearchResult | None = None
    error: str | None = None
