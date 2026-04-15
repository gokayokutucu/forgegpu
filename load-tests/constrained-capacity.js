import { Counter, Rate, Trend } from 'k6/metrics';
import {
  buildSharedIterationsOptions,
  compactForgeSummary,
  extractJobSummary,
  fetchSystemSnapshot,
  formatSummary,
  randomSuffix,
  scenarioParams,
  submitJob,
  pollJobUntilTerminal,
} from './helpers.js';

export const options = buildSharedIterationsOptions(6, 18, '4m');

const submitSuccess = new Rate('submit_success');
const jobsCompleted = new Counter('jobs_completed');
const jobsFailed = new Counter('jobs_failed');
const jobsDeadLettered = new Counter('jobs_dead_lettered');
const jobsPollTimeout = new Counter('jobs_poll_timeout');
const jobsDeferredObserved = new Counter('jobs_deferred_observed');
const jobsRetriedObserved = new Counter('jobs_retried_observed');
const endToEndLatencyMs = new Trend('end_to_end_latency_ms');
const queueWaitMs = new Trend('queue_wait_ms');
const executionLatencyMs = new Trend('execution_latency_ms');
const batchSizeSeen = new Trend('batch_size_seen');

const params = scenarioParams({
  model: __ENV.MODEL || 'gpt-sim-b',
  requiredMemoryMb: parseInt(__ENV.REQUIRED_MEMORY_MB || '14000', 10),
  pollTimeoutMs: parseInt(__ENV.POLL_TIMEOUT_MS || '12000', 10),
  pollIntervalMs: parseInt(__ENV.POLL_INTERVAL_MS || '750', 10),
});

export default function () {
  const payload = {
    prompt: `constrained-${randomSuffix()}`,
    model: params.model,
    requiredMemoryMb: params.requiredMemoryMb,
    weight: parseInt(__ENV.WEIGHT || '400', 10),
  };

  const submitResult = submitJob(params, payload);
  submitSuccess.add(!!submitResult.id);

  const result = pollJobUntilTerminal(params, submitResult.id);
  const summary = extractJobSummary(result);
  const snapshot = fetchSystemSnapshot(params);

  const deferred = snapshot.metrics?.queue?.deferredPendingCount || 0;
  if (deferred > 0) {
    jobsDeferredObserved.add(deferred);
  }

  recordJobSummary(summary);
}

export function teardown() {
  console.log(`FORGEGPU_SNAPSHOT ${JSON.stringify(compactForgeSummary(fetchSystemSnapshot(params)))}`);
}

export function handleSummary(data) {
  return { stdout: formatSummary('Constrained Capacity Scenario', data) };
}

function recordJobSummary(summary) {
  if (summary.status === 'Completed') {
    jobsCompleted.add(1);
  } else if (summary.status === 'Failed') {
    jobsFailed.add(1);
  } else if (summary.status === 'DeadLettered') {
    jobsDeadLettered.add(1);
  } else {
    jobsPollTimeout.add(1);
  }

  if (summary.retryCount > 0) {
    jobsRetriedObserved.add(1);
  }

  endToEndLatencyMs.add(summary.totalLatencyMs);
  queueWaitMs.add(summary.queueWaitMs);
  executionLatencyMs.add(summary.executionMs);
  batchSizeSeen.add(1);
}
