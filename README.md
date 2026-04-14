# ForgeGPU

ForgeGPU is a recruiter-facing AI inference orchestration demo built in .NET 10.

This repository is developed phase by phase as a lightweight inference platform foundation: jobs are accepted, queued, executed by workers, and exposed through status endpoints. Phase 1 remains in-memory for queue/store behavior, while local Redis and Postgres are now first-class dependencies via Docker Compose for Phase 2 readiness.

## Current Phase Scope

- ASP.NET Core Web API (`ForgeGPU.Api`)
- In-memory inference job queue
- In-memory inference job store
- Single background inference worker
- Job submission and status endpoints
- Structured lifecycle logging
- Docker Compose local platform bootstrap (API + Redis + Postgres)

Not implemented yet:
- Redis-backed queue
- Postgres-backed job persistence
- Multi-worker scheduler
- GPU-aware scheduling simulation
- Dynamic batching

## Solution Structure

- `ForgeGPU.Api` - HTTP API, host bootstrap, DI wiring, runtime config
- `ForgeGPU.Core` - domain models and orchestration contracts
- `ForgeGPU.Infrastructure` - queue/store implementations and worker runtime

## Prerequisites

- .NET SDK 10.0+
- Docker Desktop (or Docker Engine with Compose)

## Run With Docker Compose (Recommended)

1. Create local environment file:

```bash
cp .env.example .env
```

2. Start platform services:

```bash
docker compose up --build
```

3. API will be available at:

- `http://localhost:8080`

4. Stop services:

```bash
docker compose down
```

To remove Postgres persisted data volume as well:

```bash
docker compose down -v
```

## Run Without Docker Compose

```bash
dotnet build ForgeGPU.sln
dotnet run --project ForgeGPU.Api
```

By default, local `dotnet run` URL comes from launch settings (`http://localhost:5149`).

## Configuration (Phase 2 Ready)

Infrastructure settings are bound from `Infrastructure` configuration:

- `Infrastructure:Runtime:QueueProvider`
- `Infrastructure:Runtime:JobStoreProvider`
- `Infrastructure:Redis:ConnectionString`
- `Infrastructure:Postgres:ConnectionString`

In Compose, these are injected via environment variables:

- `INFRASTRUCTURE_QUEUE_PROVIDER`
- `INFRASTRUCTURE_JOBSTORE_PROVIDER`
- `REDIS_CONNECTION_STRING`
- `POSTGRES_CONNECTION_STRING`

Current default runtime remains:

- `QueueProvider = InMemory`
- `JobStoreProvider = InMemory`

## API Endpoints

### Submit Job

`POST /jobs`

Request:

```json
{
  "prompt": "Explain queue-based inference orchestration",
  "model": "gpt-sim"
}
```

Response (`202 Accepted`):

```json
{
  "id": "3d6f8b87-9bc8-4f3f-9660-b2f5605d5d65",
  "status": "Queued",
  "statusEndpoint": "/jobs/3d6f8b87-9bc8-4f3f-9660-b2f5605d5d65"
}
```

### Get Job

`GET /jobs/{id}`

Response example:

```json
{
  "id": "3d6f8b87-9bc8-4f3f-9660-b2f5605d5d65",
  "prompt": "Explain queue-based inference orchestration",
  "model": "gpt-sim",
  "status": "Completed",
  "createdAtUtc": "2026-04-14T19:55:34.221248Z",
  "startedAtUtc": "2026-04-14T19:55:34.222958Z",
  "completedAtUtc": "2026-04-14T19:55:36.623368Z",
  "result": "Simulated response for prompt: Explain queue-based inference orchestration",
  "error": null
}
```

## cURL Examples (Compose)

```bash
# 1) Submit job
curl -X POST http://localhost:8080/jobs \
  -H "Content-Type: application/json" \
  -d '{"prompt":"Explain queue-based inference orchestration","model":"gpt-sim"}'

# 2) Poll job status
curl http://localhost:8080/jobs/<job-id>
```

## Why This Prepares Phase 2

- Redis and Postgres are now bootstrapped as local platform dependencies.
- API already binds Redis/Postgres connection settings from environment-driven config.
- Runtime provider switches (`QueueProvider`, `JobStoreProvider`) are explicit.
- Next phase can replace in-memory implementations with Redis/Postgres-backed implementations behind existing Core interfaces.
