import http from 'k6/http';
import { check, sleep } from 'k6';

export const defaultParams = {
  apiBaseUrl: __ENV.API_BASE_URL || 'http://localhost:8080',
  model: __ENV.MODEL || 'gpt-sim-a',
  requiredMemoryMb: parseInt(__ENV.REQUIRED_MEMORY_MB || '4096', 10),
  weight: parseInt(__ENV.WEIGHT || '100', 10),
  pollIntervalMs: parseInt(__ENV.POLL_INTERVAL_MS || '500', 10),
  pollTimeoutMs: parseInt(__ENV.POLL_TIMEOUT_MS || '45000', 10),
};

export function buildSharedIterationsOptions(defaultVus, defaultIterations, defaultMaxDuration) {
  return {
    scenarios: {
      main: {
        executor: 'shared-iterations',
        vus: parseInt(__ENV.VUS || String(defaultVus), 10),
        iterations: parseInt(__ENV.ITERATIONS || String(defaultIterations), 10),
        maxDuration: __ENV.MAX_DURATION || defaultMaxDuration,
      },
    },
  };
}

export function scenarioParams(overrides = {}) {
  return { ...defaultParams, ...overrides };
}

export function submitJob(params, payload) {
  const response = http.post(
    `${params.apiBaseUrl}/jobs`,
    JSON.stringify(payload),
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { endpoint: 'submit-job' },
    },
  );

  check(response, {
    'submit returned 202': (r) => r.status === 202,
    'submit returned id': (r) => !!safeJson(r).id,
  });

  return safeJson(response);
}

export function pollJobUntilTerminal(params, jobId) {
  const deadline = Date.now() + params.pollTimeoutMs;
  let finalResponse = null;

  while (Date.now() < deadline) {
    const response = http.get(`${params.apiBaseUrl}/jobs/${jobId}`, {
      tags: { endpoint: 'get-job' },
    });

    check(response, {
      'job poll returned 200': (r) => r.status === 200,
    });

    const body = safeJson(response);
    finalResponse = body;

    if (body.status === 'Completed' || body.status === 'Failed' || body.status === 'DeadLettered') {
      return body;
    }

    sleep(params.pollIntervalMs / 1000);
  }

  return finalResponse || { id: jobId, status: 'PollTimeout' };
}

export function fetchSystemSnapshot(params) {
  return {
    metrics: safeJson(http.get(`${params.apiBaseUrl}/metrics`, { tags: { endpoint: 'metrics' } })),
    workers: safeJson(http.get(`${params.apiBaseUrl}/workers`, { tags: { endpoint: 'workers' } })),
  };
}

export function randomSuffix() {
  return `${__VU}-${__ITER}-${Date.now()}-${Math.floor(Math.random() * 10000)}`;
}

export function safeJson(response) {
  try {
    return response.json();
  } catch (error) {
    return {};
  }
}

export function extractJobSummary(result) {
  return {
    status: result.status || 'Unknown',
    queueWaitMs: durationMs(result.createdAtUtc, result.startedAtUtc),
    executionMs: durationMs(result.startedAtUtc, result.completedAtUtc),
    totalLatencyMs: durationMs(result.createdAtUtc, result.completedAtUtc),
    retryCount: result.retryCount || 0,
    failureCategory: result.lastFailureCategory || null,
  };
}

export function durationMs(start, end) {
  if (!start || !end) {
    return 0;
  }

  return Math.max(0, Date.parse(end) - Date.parse(start));
}

export function compactForgeSummary(snapshot) {
  return {
    metrics: {
      jobs: snapshot.metrics.jobs,
      queue: snapshot.metrics.queue,
      scheduler: snapshot.metrics.scheduler,
      batching: snapshot.metrics.batching,
      workers: snapshot.metrics.workers,
      latency: snapshot.metrics.latency,
    },
    workers: snapshot.workers,
  };
}

export function formatSummary(title, data) {
  const iterationRate = metricRate(data, 'iterations');
  const submitRate = metricRate(data, 'http_reqs');
  const e2e = data.metrics.end_to_end_latency_ms;
  const batchRate = data.metrics.batch_size_seen;

  return [
    '',
    `ForgeGPU ${title}`,
    `iterations: ${metricCount(data, 'iterations')}`,
    `iteration_rate_per_s: ${iterationRate}`,
    `http_requests: ${metricCount(data, 'http_reqs')}`,
    `http_request_rate_per_s: ${submitRate}`,
    `submit_success_rate: ${metricRate(data, 'submit_success')}`,
    `jobs_completed: ${metricCount(data, 'jobs_completed')}`,
    `jobs_failed: ${metricCount(data, 'jobs_failed')}`,
    `jobs_dead_lettered: ${metricCount(data, 'jobs_dead_lettered')}`,
    `jobs_poll_timeout: ${metricCount(data, 'jobs_poll_timeout')}`,
    `jobs_deferred_observed: ${metricCount(data, 'jobs_deferred_observed')}`,
    `jobs_retried_observed: ${metricCount(data, 'jobs_retried_observed')}`,
    `e2e_latency_ms_avg: ${metricTrendValue(e2e, 'avg')}`,
    `e2e_latency_ms_p95: ${metricTrendValue(e2e, 'p(95)')}`,
    `queue_wait_ms_avg: ${metricTrendValue(data.metrics.queue_wait_ms, 'avg')}`,
    `execution_latency_ms_avg: ${metricTrendValue(data.metrics.execution_latency_ms, 'avg')}`,
    `observed_batch_size_avg: ${metricTrendValue(batchRate, 'avg')}`,
    '',
  ].join('\n');
}

function metricCount(data, name) {
  return data.metrics[name]?.values?.count ?? 0;
}

function metricRate(data, name) {
  const rate = data.metrics[name]?.values?.rate;
  return rate === undefined ? 0 : rate.toFixed(2);
}

function metricTrendValue(metric, name) {
  if (!metric?.values || metric.values[name] === undefined) {
    return 0;
  }

  return Number(metric.values[name]).toFixed(2);
}
