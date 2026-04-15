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

export const options = buildSharedIterationsOptions(8, 32, '4m');

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
  model: __ENV.MODEL || 'gpt-sim-a',
  requiredMemoryMb: parseInt(__ENV.REQUIRED_MEMORY_MB || '1024', 10),
  pollIntervalMs: parseInt(__ENV.POLL_INTERVAL_MS || '300', 10),
});

export default function () {
  const payload = {
    prompt: `batch-${randomSuffix()}`,
    model: params.model,
    requiredMemoryMb: params.requiredMemoryMb,
    weight: params.weight,
  };

  const submitResult = submitJob(params, payload);
  submitSuccess.add(!!submitResult.id);

  const result = pollJobUntilTerminal(params, submitResult.id);
  const summary = extractJobSummary(result);
  const snapshot = fetchSystemSnapshot(params);

  if ((snapshot.metrics?.batching?.totalBatchesFormed || 0) > 0) {
    batchSizeSeen.add(snapshot.metrics.batching.averageBatchSize || 1);
  } else {
    batchSizeSeen.add(1);
  }

  recordJobSummary(summary, snapshot);
}

export function teardown() {
  console.log(`FORGEGPU_SNAPSHOT ${JSON.stringify(compactForgeSummary(fetchSystemSnapshot(params)))}`);
}

export function handleSummary(data) {
  return { stdout: formatSummary('Batch Heavy Scenario', data) };
}

function recordJobSummary(summary, snapshot) {
  if (summary.status === 'Completed') {
    jobsCompleted.add(1);
  } else if (summary.status === 'Failed') {
    jobsFailed.add(1);
  } else if (summary.status === 'DeadLettered') {
    jobsDeadLettered.add(1);
  } else {
    jobsPollTimeout.add(1);
  }

  const deferred = snapshot.metrics?.queue?.deferredPendingCount || 0;
  if (deferred > 0) {
    jobsDeferredObserved.add(deferred);
  }

  if (summary.retryCount > 0) {
    jobsRetriedObserved.add(1);
  }

  endToEndLatencyMs.add(summary.totalLatencyMs);
  queueWaitMs.add(summary.queueWaitMs);
  executionLatencyMs.add(summary.executionMs);
}
