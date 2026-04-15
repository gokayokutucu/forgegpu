# ForgeGPU

ForgeGPU is a recruiter-facing AI inference orchestration demo built in .NET 10.

Phase 7 introduces a reliability layer on top of the current dispatcher/worker architecture with execution timeouts, retry policy, failure classification, dead-letter direction, and visibility for terminal failures.

## Phase 7 Scope

- Redis ingress queue (job ids only)
- Postgres durable job state
- Dispatcher/coordinator flow
- In-process worker pool with explicit GPU capability metadata
- Admission control (model compatibility + VRAM + execution slots)
- Least-loaded GPU-aware scheduler foundation
- Deferred in-process pending queue when no worker is eligible
- Dynamic batching at the worker mailbox side
- Batch-aware worker accounting
- `/workers` worker/resource visibility
- `/metrics` system visibility for queue, scheduler, batching, utilization, and latency
- Timeout handling for worker execution
- Retry policy with fixed delay and capped attempts
- Explicit failure categories and durable retry metadata
- Dead-letter visibility through durable job state and `GET /jobs/dead-letter`

## Core Flow

1. API inserts durable job row in Postgres.
2. API enqueues job id to Redis.
3. Dispatcher dequeues from Redis.
4. Scheduler evaluates eligibility across workers.
5. Eligible job is dispatched to worker mailbox.
6. Worker collects a short compatible batch window and processes one execution unit.
7. Worker reserves/releases simulated VRAM and updates durable job state per job.
8. Ineligible jobs are deferred and retried.
9. Retryable execution failures are delayed and re-enqueued through the ingress queue.
10. Retry-exhausted or non-retryable jobs become dead-lettered terminal failures.

## API Endpoints

- `POST /jobs`
- `GET /jobs/{id}`
- `GET /jobs/dead-letter`
- `GET /workers`
- `GET /metrics`
- `GET /health`

## POST /jobs Request

```json
{
  "prompt": "run simulation",
  "model": "gpt-sim-b",
  "weight": 300,
  "requiredMemoryMb": 8192
}
```

Notes:
- `model` optional, defaults to `Infrastructure:Scheduling:DefaultModel`
- `weight` optional, defaults to `100`, valid range `1..1000`
- `requiredMemoryMb` optional, derived from model defaults if omitted
- `maxRetries` is configured server-side via `Infrastructure:Reliability:MaxRetries`

## Reliability Rules

- Retryable categories:
  - `Timeout`
  - `ExecutionError`
- Terminal categories:
  - `NonRetryableError`
  - `RetryExhausted`
- Batch failures are handled per job with a shared failure cause.
- Retry scheduling uses a fixed delay and keeps the orchestration path explicit.
- Dead-letter jobs remain queryable from Postgres and through `GET /jobs/dead-letter`.

## Batching Rules

- Jobs may batch only when they share the same `model`.
- Batch memory uses a simple deterministic formula: sum of `requiredMemoryMb` across jobs in the batch.
- A worker forms a batch only within a short configurable window.
- A batch is capped by `MaxBatchSize` and `MaxBatchMemoryMb`.

## GET /workers Response Highlights

Returns:
- scheduler policy
- pending/deferred job count
- per-worker identity and state
- GPU id, total/reserved/available VRAM
- supported models
- assigned job ids
- current batch size
- total batches formed
- last batch summary
- completed and failed job counts per worker
- live job and VRAM utilization percentages

## GET /metrics Response Highlights

Returns:
- total accepted/completed/failed/deferred jobs
- total retried/timed-out/terminal-failed/dead-lettered jobs
- ingress queue depth and deferred pending count
- scheduler decision, dispatch, and deferral counts
- batch totals and average batch size
- aggregate worker utilization and VRAM usage
- queue wait, execution, and total latency averages
- failure counts by category

## Quick Run

```bash
cp .env.example .env
docker compose up --build
```

## Quick Validation

```bash
# health
curl http://localhost:8080/health

# workers snapshot
curl http://localhost:8080/workers

# system metrics snapshot
curl http://localhost:8080/metrics

# submit one successful job
curl -X POST http://localhost:8080/jobs -H "Content-Type: application/json" -d '{"prompt":"phase7-success","model":"gpt-sim-a","requiredMemoryMb":2048,"weight":100}'

# submit a retryable failure that succeeds on the second attempt
curl -X POST http://localhost:8080/jobs -H "Content-Type: application/json" -d '{"prompt":"phase7-fail-retry-once","model":"gpt-sim-b","requiredMemoryMb":4096,"weight":100}'

# submit a timeout path that eventually dead-letters
curl -X POST http://localhost:8080/jobs -H "Content-Type: application/json" -d '{"prompt":"phase7-slow-timeout","model":"gpt-sim-mix","requiredMemoryMb":4096,"weight":100}'

# submit a retry-exhausted execution failure
curl -X POST http://localhost:8080/jobs -H "Content-Type: application/json" -d '{"prompt":"phase7-fail-always","model":"gpt-sim-b","requiredMemoryMb":4096,"weight":100}'

# inspect dead-letter jobs
curl http://localhost:8080/jobs/dead-letter

# inspect persisted jobs
docker compose exec -T forgegpu-postgres \
  psql -U forgegpu -d forgegpu -c "SELECT prompt, status, retry_count, last_failure_category FROM inference_jobs ORDER BY created_at_utc DESC LIMIT 10;"
```

## Not In Scope Yet

- Kafka
- distributed lease/claim semantics
- real GPU telemetry integration
- external metrics/exporter stack
