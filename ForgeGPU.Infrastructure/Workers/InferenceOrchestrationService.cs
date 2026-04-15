using System.Collections.Concurrent;
using System.Threading.Channels;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceWorkers;
using ForgeGPU.Core.Observability;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ForgeGPU.Infrastructure.Workers;

public sealed class InferenceOrchestrationService : BackgroundService, IWorkerStateReader, IOrchestrationTelemetry
{
    private const int RecentWindowSize = 20;

    private readonly IJobQueue _jobQueue;
    private readonly IJobStore _jobStore;
    private readonly IWorkerScheduler _workerScheduler;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<InferenceOrchestrationService> _logger;
    private readonly InfrastructureOptions _options;
    private readonly ConcurrentDictionary<string, WorkerNode> _workers = new(StringComparer.Ordinal);

    private readonly Queue<DeferredJob> _pendingJobs = new();
    private readonly object _pendingJobsLock = new();
    private readonly ConcurrentDictionary<Guid, RetryJob> _retryJobs = new();
    private readonly object _metricsLock = new();
    private readonly ConcurrentDictionary<string, int> _completedByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _batchesByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _deferralReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _failureCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<int> _recentBatchSizes = new();
    private readonly Queue<LatencySample> _recentLatencies = new();

    private long _totalJobsAccepted;
    private long _totalJobsCompleted;
    private long _totalJobsFailed;
    private long _totalJobsDeferred;
    private long _totalJobsRetried;
    private long _totalJobsTimedOut;
    private long _totalTerminalFailures;
    private long _retryExhaustedCount;
    private long _deadLetterCount;
    private long _schedulerDecisionCount;
    private long _schedulerSelectionCount;
    private long _schedulerDeferralCount;
    private long _totalBatchesFormed;
    private long _batchSizeTotal;
    private long _latencySampleCount;
    private long _queueWaitMsTotal;
    private long _executionMsTotal;
    private long _totalLatencyMsTotal;

    public InferenceOrchestrationService(
        IJobQueue jobQueue,
        IJobStore jobStore,
        IWorkerScheduler workerScheduler,
        IConnectionMultiplexer redis,
        IOptions<InfrastructureOptions> options,
        ILogger<InferenceOrchestrationService> logger)
    {
        _jobQueue = jobQueue;
        _jobStore = jobStore;
        _workerScheduler = workerScheduler;
        _redis = redis;
        _logger = logger;
        _options = options.Value;

        InitializeWorkers();
    }

    public IReadOnlyCollection<WorkerState> GetWorkers()
    {
        return _workers.Values
            .Select(x => x.ToState())
            .OrderBy(x => x.WorkerId, StringComparer.Ordinal)
            .ToArray();
    }

    public int GetPendingJobCount()
    {
        lock (_pendingJobsLock)
        {
            return _pendingJobs.Count;
        }
    }

    public string GetSchedulerPolicy()
    {
        return _options.WorkerExecution.SchedulerPolicy;
    }

    public void RecordJobAccepted(string model)
    {
        Interlocked.Increment(ref _totalJobsAccepted);
    }

    public void RecordJobDeferred(string reason)
    {
        Interlocked.Increment(ref _totalJobsDeferred);
        Interlocked.Increment(ref _schedulerDeferralCount);
        IncrementCounter(_deferralReasons, reason);
    }

    public async ValueTask<OrchestrationMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var workers = GetWorkers();
        var totalWorkers = workers.Count;
        var activeJobs = workers.Sum(x => x.ActiveJobCount);
        var jobCapacity = workers.Sum(x => x.MaxConcurrentJobs);
        var reservedVramMb = workers.Sum(x => x.VramReservedMb);
        var totalVramMb = workers.Sum(x => x.VramTotalMb);

        var ingressQueueDepth = await GetIngressQueueDepthAsync(cancellationToken);
        var pendingReasons = GetPendingReasonCounts();

        long totalBatchesFormed;
        long batchSizeTotal;
        long latencySampleCount;
        long queueWaitMsTotal;
        long executionMsTotal;
        long totalLatencyMsTotal;
        double recentAverageBatchSize;
        double recentAverageQueueWaitMs;
        double recentAverageExecutionMs;
        double recentAverageTotalLatencyMs;

        lock (_metricsLock)
        {
            totalBatchesFormed = _totalBatchesFormed;
            batchSizeTotal = _batchSizeTotal;
            latencySampleCount = _latencySampleCount;
            queueWaitMsTotal = _queueWaitMsTotal;
            executionMsTotal = _executionMsTotal;
            totalLatencyMsTotal = _totalLatencyMsTotal;
            recentAverageBatchSize = _recentBatchSizes.Count == 0 ? 0 : Math.Round(_recentBatchSizes.Average(), 2);
            recentAverageQueueWaitMs = _recentLatencies.Count == 0 ? 0 : Math.Round(_recentLatencies.Average(x => x.QueueWaitMs), 2);
            recentAverageExecutionMs = _recentLatencies.Count == 0 ? 0 : Math.Round(_recentLatencies.Average(x => x.ExecutionMs), 2);
            recentAverageTotalLatencyMs = _recentLatencies.Count == 0 ? 0 : Math.Round(_recentLatencies.Average(x => x.TotalLatencyMs), 2);
        }

        return new OrchestrationMetricsSnapshot(
            DateTime.UtcNow,
            new JobMetricsSnapshot(
                Volatile.Read(ref _totalJobsAccepted),
                Volatile.Read(ref _totalJobsCompleted),
                Volatile.Read(ref _totalJobsFailed),
                Volatile.Read(ref _totalJobsRetried),
                Volatile.Read(ref _totalJobsTimedOut),
                Volatile.Read(ref _totalTerminalFailures),
                Volatile.Read(ref _retryExhaustedCount),
                Volatile.Read(ref _deadLetterCount),
                Volatile.Read(ref _totalJobsDeferred),
                activeJobs,
                SnapshotDictionary(_completedByModel),
                SnapshotDictionary(_failureCategories)),
            new QueueMetricsSnapshot(
                ingressQueueDepth,
                GetPendingJobCount() + GetRetryPendingCount(),
                pendingReasons),
            new SchedulerMetricsSnapshot(
                _options.WorkerExecution.SchedulerPolicy,
                Volatile.Read(ref _schedulerDecisionCount),
                Volatile.Read(ref _schedulerSelectionCount),
                Volatile.Read(ref _schedulerDeferralCount),
                SnapshotDictionary(_deferralReasons)),
            new BatchMetricsSnapshot(
                totalBatchesFormed,
                totalBatchesFormed == 0 ? 0 : Math.Round((double)batchSizeTotal / totalBatchesFormed, 2),
                recentAverageBatchSize,
                SnapshotDictionary(_batchesByModel)),
            new WorkerUtilizationMetricsSnapshot(
                totalWorkers,
                workers.Count(x => x.Status is WorkerStatus.Busy or WorkerStatus.Saturated),
                workers.Count(x => x.Status == WorkerStatus.Saturated),
                activeJobs,
                jobCapacity,
                jobCapacity == 0 ? 0 : Math.Round((double)activeJobs / jobCapacity * 100, 2),
                reservedVramMb,
                totalVramMb,
                totalVramMb == 0 ? 0 : Math.Round((double)reservedVramMb / totalVramMb * 100, 2)),
            new LatencyMetricsSnapshot(
                latencySampleCount,
                latencySampleCount == 0 ? 0 : Math.Round((double)queueWaitMsTotal / latencySampleCount, 2),
                latencySampleCount == 0 ? 0 : Math.Round((double)executionMsTotal / latencySampleCount, 2),
                latencySampleCount == 0 ? 0 : Math.Round((double)totalLatencyMsTotal / latencySampleCount, 2),
                recentAverageQueueWaitMs,
                recentAverageExecutionMs,
                recentAverageTotalLatencyMs));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inference orchestration started. WorkerCount: {WorkerCount}. SchedulerPolicy: {SchedulerPolicy}. PendingRetryDelayMs: {PendingRetryDelayMs}. BatchingEnabled: {BatchingEnabled}. BatchWindowMs: {BatchWindowMs}. MaxBatchSize: {MaxBatchSize}. MaxBatchMemoryMb: {MaxBatchMemoryMb}. ExecutionTimeoutMs: {ExecutionTimeoutMs}. MaxRetries: {MaxRetries}. RetryDelayMs: {RetryDelayMs}.",
            _workers.Count,
            _options.WorkerExecution.SchedulerPolicy,
            _options.Scheduling.DeferRetryDelayMs,
            _options.Batching.Enabled,
            _options.Batching.BatchWindowMs,
            _options.Batching.MaxBatchSize,
            _options.Batching.MaxBatchMemoryMb,
            _options.Reliability.ExecutionTimeoutMs,
            _options.Reliability.MaxRetries,
            _options.Reliability.RetryDelayMs);

        var workerTasks = _workers.Values
            .Select(x => RunWorkerLoopAsync(x, stoppingToken))
            .ToArray();

        var heartbeatTask = RunHeartbeatLoopAsync(stoppingToken);

        try
        {
            await RunDispatcherLoopAsync(stoppingToken);
        }
        finally
        {
            try
            {
                await Task.WhenAll(workerTasks.Append(heartbeatTask));
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown cancellation.
            }

            _logger.LogInformation("Inference orchestration stopped.");
        }
    }

    private async Task RunDispatcherLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var jobId = await AcquireNextJobIdAsync(cancellationToken);
            if (jobId is null)
            {
                continue;
            }

            var job = await _jobStore.GetAsync(jobId.Value, cancellationToken);
            if (job is null)
            {
                _logger.LogWarning("Dispatcher received job {JobId} but it was not found in Postgres store.", jobId.Value);
                continue;
            }

            var states = GetWorkers();
            var decision = _workerScheduler.Evaluate(job, states);
            Interlocked.Increment(ref _schedulerDecisionCount);

            _logger.LogDebug(
                "Scheduler evaluation for job {JobId}: {Evaluations}",
                job.Id,
                string.Join(", ", decision.Evaluations.Select(x => $"{x.WorkerId}={x.Reason}")));

            if (decision.SelectedWorker is null)
            {
                DeferJob(job.Id, "NoEligibleWorker");
                RecordJobDeferred("NoEligibleWorker");

                _logger.LogWarning(
                    "Deferred job {JobId}. Reason: no eligible worker. Model: {Model}. RequiredMemoryMb: {RequiredMemoryMb}. PendingJobs: {PendingJobs}.",
                    job.Id,
                    job.Model,
                    job.RequiredMemoryMb,
                    GetPendingJobCount());

                continue;
            }

            if (!_workers.TryGetValue(decision.SelectedWorker.WorkerId, out var workerNode))
            {
                DeferJob(job.Id, "SelectedWorkerMissing");
                RecordJobDeferred("SelectedWorkerMissing");
                continue;
            }

            if (!workerNode.TryReserve(job, out var reservationFailureReason))
            {
                var reason = reservationFailureReason ?? "ReservationRejected";
                DeferJob(job.Id, reason);
                RecordJobDeferred(reason);

                _logger.LogWarning(
                    "Deferred job {JobId}. Worker {WorkerId} reservation failed. Reason: {Reason}. PendingJobs: {PendingJobs}.",
                    job.Id,
                    workerNode.WorkerId,
                    reason,
                    GetPendingJobCount());

                continue;
            }

            try
            {
                await workerNode.Channel.Writer.WriteAsync(job.Id, cancellationToken);
                Interlocked.Increment(ref _schedulerSelectionCount);

                _logger.LogInformation(
                    "Dispatched job {JobId} to worker {WorkerId}. Model: {Model}. RequiredMemoryMb: {RequiredMemoryMb}. WorkerVram: {VramReservedMb}/{VramTotalMb}. ActiveJobs: {ActiveJobCount}/{MaxConcurrentJobs}. RetryCount: {RetryCount}/{MaxRetries}.",
                    job.Id,
                    workerNode.WorkerId,
                    job.Model,
                    job.RequiredMemoryMb,
                    workerNode.VramReservedMb,
                    workerNode.VramTotalMb,
                    workerNode.ActiveJobCount,
                    workerNode.MaxConcurrentJobs,
                    job.RetryCount,
                    job.MaxRetries);
            }
            catch
            {
                workerNode.ReleaseReservation(job.Id);
                throw;
            }
        }
    }

    private async Task<Guid?> AcquireNextJobIdAsync(CancellationToken cancellationToken)
    {
        if (TryTakeReadyDeferred(out var deferred))
        {
            _logger.LogInformation("Dispatcher retrying deferred job {JobId}. PreviousReason: {Reason}.", deferred.JobId, deferred.Reason);
            return deferred.JobId;
        }

        try
        {
            var jobId = await _jobQueue.DequeueAsync(cancellationToken);

            _logger.LogInformation("Dequeued job {JobId} from ingress queue.", jobId);
            return jobId;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task RunWorkerLoopAsync(WorkerNode workerNode, CancellationToken cancellationToken)
    {
        var reader = workerNode.Channel.Reader;
        var bufferedJobIds = new Queue<Guid>();

        while (!cancellationToken.IsCancellationRequested)
        {
            Guid jobId;

            if (bufferedJobIds.Count > 0)
            {
                jobId = bufferedJobIds.Dequeue();
            }
            else
            {
                try
                {
                    jobId = await reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            await ProcessBatchAsync(workerNode, reader, jobId, bufferedJobIds, cancellationToken);
        }
    }

    private async Task ProcessBatchAsync(
        WorkerNode workerNode,
        ChannelReader<Guid> reader,
        Guid firstJobId,
        Queue<Guid> bufferedJobIds,
        CancellationToken cancellationToken)
    {
        workerNode.UpdateHeartbeat();

        var batch = new List<InferenceJob>();
        var batchModel = string.Empty;
        var batchMemoryMb = 0;

        try
        {
            var firstJob = await _jobStore.GetAsync(firstJobId, cancellationToken);
            if (firstJob is null)
            {
                _logger.LogWarning(
                    "Worker {WorkerId} received job {JobId} but it no longer exists in Postgres store.",
                    workerNode.WorkerId,
                    firstJobId);
                workerNode.ReleaseReservation(firstJobId);
                return;
            }

            batch.Add(firstJob);
            batchModel = firstJob.Model ?? string.Empty;
            batchMemoryMb = firstJob.RequiredMemoryMb;

            var batchWindow = _options.Batching.Enabled
                ? TimeSpan.FromMilliseconds(Math.Max(0, _options.Batching.BatchWindowMs))
                : TimeSpan.Zero;
            var deadlineUtc = DateTime.UtcNow.Add(batchWindow);

            while (_options.Batching.Enabled && batch.Count < Math.Max(1, _options.Batching.MaxBatchSize))
            {
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    break;
                }

                var nextJobId = await TryReadNextCandidateAsync(reader, bufferedJobIds, deadlineUtc, cancellationToken);
                if (nextJobId is null)
                {
                    break;
                }

                var nextJob = await _jobStore.GetAsync(nextJobId.Value, cancellationToken);
                if (nextJob is null)
                {
                    _logger.LogWarning(
                        "Worker {WorkerId} skipped missing queued job {JobId} during batch assembly.",
                        workerNode.WorkerId,
                        nextJobId.Value);
                    workerNode.ReleaseReservation(nextJobId.Value);
                    continue;
                }

                if (!CanJoinBatch(batchModel, batchMemoryMb, nextJob))
                {
                    bufferedJobIds.Enqueue(nextJob.Id);
                    break;
                }

                batch.Add(nextJob);
                batchMemoryMb += nextJob.RequiredMemoryMb;
            }

            workerNode.StartBatch(batch);
            RecordBatchFormed(batchModel, batch.Count);

            _logger.LogInformation(
                "Worker {WorkerId} formed batch. BatchSize: {BatchSize}. Model: {Model}. BatchMemoryMb: {BatchMemoryMb}. ReservedVram: {VramReservedMb}/{VramTotalMb}.",
                workerNode.WorkerId,
                batch.Count,
                batchModel,
                batchMemoryMb,
                workerNode.VramReservedMb,
                workerNode.VramTotalMb);

            var startedAtUtc = DateTime.UtcNow;
            foreach (var job in batch)
            {
                job.MarkProcessing(startedAtUtc);
                await _jobStore.UpdateAsync(job, cancellationToken);
            }

            using var executionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            executionSource.CancelAfter(Math.Max(250, _options.Reliability.ExecutionTimeoutMs));

            await SimulateBatchExecutionAsync(batch, executionSource.Token);

            foreach (var job in batch)
            {
                job.MarkCompleted($"Simulated response for prompt: {job.Prompt}", DateTime.UtcNow);
                await _jobStore.UpdateAsync(job, cancellationToken);
                workerNode.MarkJobCompleted();
                RecordJobFinalized(job, job.Model ?? "unknown", succeeded: true, failureCategory: null, terminal: false);

                var latency = BuildLatencySample(job);
                _logger.LogInformation(
                    "Worker {WorkerId} completed job {JobId} in batch. BatchSize: {BatchSize}. QueueWaitMs: {QueueWaitMs}. ExecutionMs: {ExecutionMs}. TotalLatencyMs: {TotalLatencyMs}.",
                    workerNode.WorkerId,
                    job.Id,
                    batch.Count,
                    latency.QueueWaitMs,
                    latency.ExecutionMs,
                    latency.TotalLatencyMs);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await HandleBatchFailureAsync(
                workerNode,
                batch,
                JobFailureCategory.Timeout,
                "Execution timed out before the batch completed.",
                CancellationToken.None);
        }
        catch (NonRetryableExecutionException ex)
        {
            await HandleBatchFailureAsync(
                workerNode,
                batch,
                JobFailureCategory.NonRetryableError,
                ex.Message,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            await HandleBatchFailureAsync(
                workerNode,
                batch,
                JobFailureCategory.ExecutionError,
                ex.Message,
                CancellationToken.None);
        }
        finally
        {
            workerNode.CompleteBatch(batch);
            workerNode.UpdateHeartbeat();

            _logger.LogInformation(
                "Worker {WorkerId} released batch resources. BatchSize: {BatchSize}. ActiveJobs: {ActiveJobCount}/{MaxConcurrentJobs}. Vram: {VramReservedMb}/{VramTotalMb}. TotalBatchesFormed: {TotalBatchesFormed}.",
                workerNode.WorkerId,
                batch.Count,
                workerNode.ActiveJobCount,
                workerNode.MaxConcurrentJobs,
                workerNode.VramReservedMb,
                workerNode.VramTotalMb,
                workerNode.TotalBatchesFormed);
        }
    }

    private async Task HandleBatchFailureAsync(
        WorkerNode workerNode,
        IReadOnlyCollection<InferenceJob> batch,
        JobFailureCategory category,
        string failureReason,
        CancellationToken cancellationToken)
    {
        foreach (var job in batch)
        {
            var timestampUtc = DateTime.UtcNow;

            if (category == JobFailureCategory.Timeout)
            {
                Interlocked.Increment(ref _totalJobsTimedOut);
            }

            if (IsRetryable(category) && job.CanRetry())
            {
                job.MarkRetrying(failureReason, category, timestampUtc);
                await _jobStore.UpdateAsync(job, cancellationToken);
                EnqueueRetry(job, category);

                workerNode.MarkJobRetried();
                RecordJobRetried(category);

                _logger.LogWarning(
                    "Worker {WorkerId} scheduled retry for job {JobId}. FailureCategory: {FailureCategory}. Attempt: {RetryCount}/{MaxRetries}. RetryDelayMs: {RetryDelayMs}.",
                    workerNode.WorkerId,
                    job.Id,
                    category,
                    job.RetryCount,
                    job.MaxRetries,
                    _options.Reliability.RetryDelayMs);

                continue;
            }

            var terminalReason = IsRetryable(category) && !job.CanRetry()
                ? $"Retry exhausted after {category}: {failureReason}"
                : failureReason;

            var terminalCategory = IsRetryable(category) && !job.CanRetry()
                ? JobFailureCategory.RetryExhausted
                : category;

            job.MarkDeadLettered(terminalReason, terminalCategory, timestampUtc);
            await _jobStore.UpdateAsync(job, cancellationToken);
            workerNode.MarkJobFailed();
            RecordJobFinalized(job, job.Model ?? "unknown", succeeded: false, failureCategory: terminalCategory, terminal: true);

            if (terminalCategory == JobFailureCategory.RetryExhausted)
            {
                Interlocked.Increment(ref _retryExhaustedCount);
            }

            _logger.LogError(
                "Worker {WorkerId} moved job {JobId} to dead letter. FailureCategory: {FailureCategory}. RetryCount: {RetryCount}/{MaxRetries}.",
                workerNode.WorkerId,
                job.Id,
                terminalCategory,
                job.RetryCount,
                job.MaxRetries);
        }
    }

    private async Task SimulateBatchExecutionAsync(IReadOnlyCollection<InferenceJob> batch, CancellationToken cancellationToken)
    {
        if (batch.Any(job => PromptContains(job, "fail-terminal")))
        {
            throw new NonRetryableExecutionException("Simulated non-retryable execution error.");
        }

        if (batch.Any(job => PromptContains(job, "fail-always")))
        {
            throw new InvalidOperationException("Simulated retryable execution failure.");
        }

        if (batch.Any(job => PromptContains(job, "fail-retry-once") && job.RetryCount == 0))
        {
            throw new InvalidOperationException("Simulated retryable execution failure on first attempt.");
        }

        var delayMs = Random.Shared.Next(1500, 2501) + ((batch.Count - 1) * 150);
        if (batch.Any(job => PromptContains(job, "slow-timeout")))
        {
            delayMs = Math.Max(delayMs, _options.Reliability.ExecutionTimeoutMs + 500);
        }

        await Task.Delay(delayMs, cancellationToken);
    }

    private async Task<Guid?> TryReadNextCandidateAsync(
        ChannelReader<Guid> reader,
        Queue<Guid> bufferedJobIds,
        DateTime deadlineUtc,
        CancellationToken cancellationToken)
    {
        if (bufferedJobIds.Count > 0)
        {
            return bufferedJobIds.Dequeue();
        }

        var remaining = deadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedSource.CancelAfter(remaining);

        try
        {
            return await reader.ReadAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private bool CanJoinBatch(string batchModel, int batchMemoryMb, InferenceJob nextJob)
    {
        if (!string.Equals(batchModel, nextJob.Model, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var maxBatchMemoryMb = Math.Max(1, _options.Batching.MaxBatchMemoryMb);
        return batchMemoryMb + nextJob.RequiredMemoryMb <= maxBatchMemoryMb;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.WorkerExecution.HeartbeatIntervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var worker in _workers.Values)
            {
                worker.UpdateHeartbeat();
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private void InitializeWorkers()
    {
        var definitions = ResolveWorkerDefinitions();

        foreach (var definition in definitions)
        {
            _workers[definition.WorkerId] = new WorkerNode(
                definition.WorkerId,
                definition.Name,
                definition.GpuId,
                definition.MaxConcurrentJobs,
                definition.VramTotalMb,
                definition.SupportedModels);
        }
    }

    private IReadOnlyCollection<WorkerDefinition> ResolveWorkerDefinitions()
    {
        if (_options.WorkerExecution.Workers.Count > 0)
        {
            return _options.WorkerExecution.Workers
                .Select(x => new WorkerDefinition
                {
                    WorkerId = x.WorkerId,
                    Name = x.Name,
                    GpuId = x.GpuId,
                    MaxConcurrentJobs = Math.Max(1, x.MaxConcurrentJobs),
                    VramTotalMb = Math.Max(2048, x.VramTotalMb),
                    SupportedModels = x.SupportedModels
                })
                .ToArray();
        }

        var workerCount = Math.Max(1, _options.WorkerExecution.WorkerCount);
        var defaultMaxConcurrent = Math.Max(1, _options.WorkerExecution.MaxConcurrentJobsPerWorker);
        var supportedModels = _options.Scheduling.ModelDefaults.Select(x => x.Model).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var definitions = new List<WorkerDefinition>(workerCount);

        for (var i = 1; i <= workerCount; i++)
        {
            definitions.Add(new WorkerDefinition
            {
                WorkerId = $"worker-{i:00}",
                Name = $"Inference Worker {i}",
                GpuId = $"GPU-{i - 1}",
                MaxConcurrentJobs = defaultMaxConcurrent,
                VramTotalMb = 8192 + ((i - 1) * 4096),
                SupportedModels = supportedModels
            });
        }

        return definitions;
    }

    private void DeferJob(Guid jobId, string reason)
    {
        var retryAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(50, _options.Scheduling.DeferRetryDelayMs));

        lock (_pendingJobsLock)
        {
            _pendingJobs.Enqueue(new DeferredJob(jobId, retryAtUtc, reason));
        }
    }

    private bool TryTakeReadyDeferred(out DeferredJob deferredJob)
    {
        lock (_pendingJobsLock)
        {
            if (_pendingJobs.Count == 0)
            {
                deferredJob = default;
                return false;
            }

            var candidate = _pendingJobs.Peek();
            if (candidate.RetryAfterUtc > DateTime.UtcNow)
            {
                deferredJob = default;
                return false;
            }

            deferredJob = _pendingJobs.Dequeue();
            return true;
        }
    }

    private void EnqueueRetry(InferenceJob job, JobFailureCategory category)
    {
        var retryAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(50, _options.Reliability.RetryDelayMs));
        var retryJob = new RetryJob(job.Id, retryAtUtc, category, job.RetryCount, job.MaxRetries);
        _retryJobs[job.Id] = retryJob;

        _ = ScheduleRetryEnqueueAsync(retryJob);
    }

    private int GetRetryPendingCount()
    {
        return _retryJobs.Count;
    }

    private async Task<long?> GetIngressQueueDepthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var db = _redis.GetDatabase();
            return await db.ListLengthAsync(_options.Redis.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read ingress queue depth for queue {QueueName}.", _options.Redis.QueueName);
            return null;
        }
    }

    private IReadOnlyDictionary<string, int> GetPendingReasonCounts()
    {
        lock (_pendingJobsLock)
        {
            return _pendingJobs
                .GroupBy(x => x.Reason, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        }
    }

    private void RecordBatchFormed(string model, int batchSize)
    {
        Interlocked.Increment(ref _totalBatchesFormed);
        Interlocked.Add(ref _batchSizeTotal, batchSize);
        IncrementCounter(_batchesByModel, model);

        lock (_metricsLock)
        {
            _recentBatchSizes.Enqueue(batchSize);
            while (_recentBatchSizes.Count > RecentWindowSize)
            {
                _recentBatchSizes.Dequeue();
            }
        }
    }

    private void RecordJobRetried(JobFailureCategory category)
    {
        Interlocked.Increment(ref _totalJobsRetried);
        IncrementCounter(_failureCategories, category.ToString());
    }

    private void RecordJobFinalized(
        InferenceJob job,
        string model,
        bool succeeded,
        JobFailureCategory? failureCategory,
        bool terminal)
    {
        if (succeeded)
        {
            Interlocked.Increment(ref _totalJobsCompleted);
            IncrementCounter(_completedByModel, model);
        }
        else
        {
            Interlocked.Increment(ref _totalJobsFailed);

            if (terminal)
            {
                Interlocked.Increment(ref _totalTerminalFailures);
                Interlocked.Increment(ref _deadLetterCount);
            }

            if (failureCategory is not null)
            {
                IncrementCounter(_failureCategories, failureCategory.Value.ToString());
            }
        }

        var latency = BuildLatencySample(job);

        lock (_metricsLock)
        {
            _latencySampleCount++;
            _queueWaitMsTotal += latency.QueueWaitMs;
            _executionMsTotal += latency.ExecutionMs;
            _totalLatencyMsTotal += latency.TotalLatencyMs;

            _recentLatencies.Enqueue(latency);
            while (_recentLatencies.Count > RecentWindowSize)
            {
                _recentLatencies.Dequeue();
            }
        }
    }

    private static LatencySample BuildLatencySample(InferenceJob job)
    {
        var queueWaitMs = job.StartedAtUtc.HasValue
            ? Math.Max(0, (long)(job.StartedAtUtc.Value - job.CreatedAtUtc).TotalMilliseconds)
            : 0;

        var executionMs = job.StartedAtUtc.HasValue && job.CompletedAtUtc.HasValue
            ? Math.Max(0, (long)(job.CompletedAtUtc.Value - job.StartedAtUtc.Value).TotalMilliseconds)
            : 0;

        var totalLatencyMs = job.CompletedAtUtc.HasValue
            ? Math.Max(0, (long)(job.CompletedAtUtc.Value - job.CreatedAtUtc).TotalMilliseconds)
            : 0;

        return new LatencySample(queueWaitMs, executionMs, totalLatencyMs);
    }

    private static bool IsRetryable(JobFailureCategory category)
    {
        return category is JobFailureCategory.Timeout or JobFailureCategory.ExecutionError;
    }

    private static bool PromptContains(InferenceJob job, string marker)
    {
        return job.Prompt.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static void IncrementCounter(ConcurrentDictionary<string, int> counters, string key)
    {
        counters.AddOrUpdate(key, 1, static (_, current) => current + 1);
    }

    private async Task ScheduleRetryEnqueueAsync(RetryJob retryJob)
    {
        try
        {
            var delay = retryJob.RetryAfterUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            await _jobQueue.EnqueueAsync(retryJob.JobId, CancellationToken.None);
            _logger.LogInformation(
                "Re-enqueued retry job {JobId} to ingress queue. FailureCategory: {FailureCategory}. Attempt: {Attempt}/{MaxRetries}.",
                retryJob.JobId,
                retryJob.FailureCategory,
                retryJob.RetryCount,
                retryJob.MaxRetries);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to re-enqueue retry job {JobId}. FailureCategory: {FailureCategory}. Attempt: {Attempt}/{MaxRetries}.",
                retryJob.JobId,
                retryJob.FailureCategory,
                retryJob.RetryCount,
                retryJob.MaxRetries);
        }
        finally
        {
            _retryJobs.TryRemove(retryJob.JobId, out _);
        }
    }

    private static IReadOnlyDictionary<string, int> SnapshotDictionary(ConcurrentDictionary<string, int> source)
    {
        return source
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct DeferredJob(Guid JobId, DateTime RetryAfterUtc, string Reason);
    private readonly record struct RetryJob(Guid JobId, DateTime RetryAfterUtc, JobFailureCategory FailureCategory, int RetryCount, int MaxRetries);
    private readonly record struct LatencySample(long QueueWaitMs, long ExecutionMs, long TotalLatencyMs);

    private sealed class WorkerNode
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, int> _jobMemoryReservations = new();
        private readonly HashSet<string> _supportedModels;
        private string? _currentModel;

        private int _activeJobCount;
        private int _vramReservedMb;
        private long _lastHeartbeatUtcTicks;
        private int _currentBatchSize;
        private int _totalBatchesFormed;
        private string? _lastBatchModel;
        private int _lastBatchSize;
        private long _lastBatchCompletedUtcTicks;
        private int _completedJobCount;
        private int _failedJobCount;

        public WorkerNode(
            string workerId,
            string name,
            string gpuId,
            int maxConcurrentJobs,
            int vramTotalMb,
            IReadOnlyCollection<string> supportedModels)
        {
            WorkerId = workerId;
            Name = name;
            GpuId = gpuId;
            MaxConcurrentJobs = maxConcurrentJobs;
            VramTotalMb = vramTotalMb;
            _supportedModels = supportedModels.ToHashSet(StringComparer.OrdinalIgnoreCase);

            Channel = System.Threading.Channels.Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _lastHeartbeatUtcTicks = DateTime.UtcNow.Ticks;
        }

        public string WorkerId { get; }
        public string Name { get; }
        public string GpuId { get; }
        public int MaxConcurrentJobs { get; }
        public int VramTotalMb { get; }
        public Channel<Guid> Channel { get; }
        public int TotalBatchesFormed => Volatile.Read(ref _totalBatchesFormed);
        public int ActiveJobCount => Volatile.Read(ref _activeJobCount);
        public int VramReservedMb => Volatile.Read(ref _vramReservedMb);

        public bool TryReserve(InferenceJob job, out string? rejectionReason)
        {
            lock (_gate)
            {
                if (job.Model is null || !_supportedModels.Contains(job.Model))
                {
                    rejectionReason = "ModelNotSupported";
                    return false;
                }

                if (_activeJobCount >= MaxConcurrentJobs)
                {
                    rejectionReason = "NoExecutionSlot";
                    return false;
                }

                if (_currentModel is not null && !string.Equals(_currentModel, job.Model, StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReason = "BatchModelMismatch";
                    return false;
                }

                var availableMemory = VramTotalMb - _vramReservedMb;
                if (availableMemory < job.RequiredMemoryMb)
                {
                    rejectionReason = "InsufficientVram";
                    return false;
                }

                _currentModel ??= job.Model;
                _activeJobCount++;
                _vramReservedMb += job.RequiredMemoryMb;
                _jobMemoryReservations[job.Id] = job.RequiredMemoryMb;
                UpdateHeartbeat();

                rejectionReason = null;
                return true;
            }
        }

        public void ReleaseReservation(Guid jobId)
        {
            lock (_gate)
            {
                if (!_jobMemoryReservations.Remove(jobId, out var reservedMemoryMb))
                {
                    return;
                }

                _activeJobCount = Math.Max(0, _activeJobCount - 1);
                _vramReservedMb = Math.Max(0, _vramReservedMb - reservedMemoryMb);
                if (_jobMemoryReservations.Count == 0)
                {
                    _currentModel = null;
                }
            }
        }

        public void UpdateHeartbeat()
        {
            Interlocked.Exchange(ref _lastHeartbeatUtcTicks, DateTime.UtcNow.Ticks);
        }

        public void StartBatch(IReadOnlyCollection<InferenceJob> batch)
        {
            lock (_gate)
            {
                _currentBatchSize = batch.Count;
                _lastBatchModel = batch.FirstOrDefault()?.Model;
            }
        }

        public void CompleteBatch(IReadOnlyCollection<InferenceJob> batch)
        {
            lock (_gate)
            {
                foreach (var job in batch)
                {
                    if (!_jobMemoryReservations.Remove(job.Id, out var reservedMemoryMb))
                    {
                        continue;
                    }

                    _activeJobCount = Math.Max(0, _activeJobCount - 1);
                    _vramReservedMb = Math.Max(0, _vramReservedMb - reservedMemoryMb);
                }

                if (batch.Count > 0)
                {
                    _lastBatchSize = batch.Count;
                    _lastBatchModel = batch.FirstOrDefault()?.Model;
                    _lastBatchCompletedUtcTicks = DateTime.UtcNow.Ticks;
                    _totalBatchesFormed++;
                }

                if (_jobMemoryReservations.Count == 0)
                {
                    _currentModel = null;
                }

                _currentBatchSize = 0;
            }
        }

        public void MarkJobCompleted()
        {
            Interlocked.Increment(ref _completedJobCount);
        }

        public void MarkJobFailed()
        {
            Interlocked.Increment(ref _failedJobCount);
        }

        public void MarkJobRetried()
        {
            // Retries are tracked at the orchestration level. Worker state keeps only terminal failures.
        }

        public WorkerState ToState()
        {
            lock (_gate)
            {
                var status = ResolveStatus();

                return new WorkerState(
                    WorkerId,
                    Name,
                    _activeJobCount,
                    MaxConcurrentJobs,
                    new DateTime(Interlocked.Read(ref _lastHeartbeatUtcTicks), DateTimeKind.Utc),
                    status,
                    GpuId,
                    VramTotalMb,
                    _vramReservedMb,
                    _supportedModels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                    _jobMemoryReservations.Keys.OrderBy(x => x).ToArray(),
                    _currentModel,
                    _currentBatchSize,
                    _totalBatchesFormed,
                    _lastBatchModel,
                    _lastBatchSize,
                    _lastBatchCompletedUtcTicks == 0
                        ? null
                        : new DateTime(_lastBatchCompletedUtcTicks, DateTimeKind.Utc),
                    _completedJobCount,
                    _failedJobCount);
            }
        }

        private WorkerStatus ResolveStatus()
        {
            if (_activeJobCount == 0 && _vramReservedMb == 0)
            {
                return WorkerStatus.Idle;
            }

            if (_activeJobCount >= MaxConcurrentJobs || _vramReservedMb >= VramTotalMb)
            {
                return WorkerStatus.Saturated;
            }

            return WorkerStatus.Busy;
        }
    }

    private sealed class NonRetryableExecutionException : Exception
    {
        public NonRetryableExecutionException(string message)
            : base(message)
        {
        }
    }
}
