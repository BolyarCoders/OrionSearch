# Local Docker environment

This Compose stack starts PostgreSQL and the .NET API. The Python research worker is an opt-in prototype; it is not wired to the API's PostgreSQL-backed job queue or the agreed research contract. The Next.js frontend does not exist yet and is intentionally left as a placeholder.

## Prerequisites

- Docker Engine or Docker Desktop with the Compose v2 plugin.
- Copy the root .env.example to .env and set a unique local POSTGRES_PASSWORD.
- Do not commit .env, API keys, or other credentials.

Run commands from the repository root.

## Start API and PostgreSQL

    cp .env.example .env
    docker compose --env-file .env -f infra/docker/compose.yaml config
    docker compose --env-file .env -f infra/docker/compose.yaml up --build -d postgres api
    docker compose --env-file .env -f infra/docker/compose.yaml ps

The API listens on http://localhost:5190 by default. Check liveness and database readiness:

    curl -i http://localhost:5190/health
    curl -i http://localhost:5190/health/ready

A healthy database does not mean migrations have been applied. The API intentionally does not apply migrations at startup. Apply migrations using the documented .NET tooling and a deliberate development database workflow; do not point local migration commands at a shared or production database.

Useful diagnostics:

    docker compose --env-file .env -f infra/docker/compose.yaml logs -f api postgres
    docker compose --env-file .env -f infra/docker/compose.yaml down

The named postgres_data volume persists data when containers are recreated. The command 'docker compose down -v' deletes that local database volume; use it only when intentionally discarding local data.

## Optional research worker

Before starting the worker, set non-empty ORION_WORKER_API_KEY and GEMINI_API_KEY values in the root .env. Start it explicitly:

    docker compose --env-file .env -f infra/docker/compose.yaml --profile worker up --build -d
    curl -i http://localhost:8000/health

The worker health endpoint does not verify Gemini credentials or a complete research request. The worker prototype currently uses in-memory job state and is not integrated with the API's PostgreSQL-backed research jobs. Do not treat this as end-to-end integration or enable it in production.

## Configuration

| Variable | Purpose | Example |
| --- | --- | --- |
| POSTGRES_DB | Local database name | orionsearch |
| POSTGRES_USER | Local database user | orionsearch |
| POSTGRES_PASSWORD | Local database password | Set locally; required |
| POSTGRES_PORT | Host port for PostgreSQL | 5432 |
| API_PORT | Host port for the API | 5190 |
| ORION_WORKER_API_KEY | Worker API key | Required only for worker profile |
| GEMINI_API_KEY | Gemini credential | Required only for worker profile |
| WORKER_PORT | Host port for worker | 8000 |

This is a local development baseline, not production hardening. Production deployment still requires approved authentication, secret management, network policy, migration operations, monitoring, and end-to-end acceptance.
