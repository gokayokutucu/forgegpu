using System.Collections.Concurrent;
using System.Threading.Channels;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceMachines;
using ForgeGPU.Core.InferenceWorkers;
using ForgeGPU.Core.Observability;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ForgeGPU.Infrastructure.Workers;

public sealed class InferenceOrchestrationService : BackgroundService, IWorkerStateReader, IMachineStateReader, IOrchestrationTelemetry
{
    private const int RecentWindowSize = 20;

    private readonly IJobQueue _jobQueue;
    private readonly IJobStore _jobStore;
    private readonly IMachineScheduler _machineScheduler;
    private readonly IResourceEstimator _resourceEstimator;
    private readonly IMachineCatalogStore _machineCatalogStore;
    private readonly IMachineLiveProjectionStore _machineLiveProjectionStore;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<InferenceOrchestrationService> _logger;
    private readonly InfrastructureOptions _options;
    private readonly ConcurrentDictionary<string, MachineActor> _machines = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MachineLivenessState> _machineLivenessStates = new(StringComparer.Ordinal);
    private readonly Queue<DeferredJob> _pendingJobs = new();
    private readonly object _pendingJobsLock = new();
    private readonly ConcurrentDictionary<Guid, RetryJob> _retryJobs = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _deferredLogTimestamps = new();
    private readonly object _metricsLock = new();
    private readonly ConcurrentDictionary<string, int> _acceptedByWeightBand = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _completedByWeightBand = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _completedByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _batchesByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _deferredByWeightBand = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _dispatchesByWeightBand = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _deferralReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _failureCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<int> _recentBatchSizes = new();
    private readonly Queue<LatencySample> _recentLatencies = new();
    private readonly CoordinatorActor _coordinator;

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
        IMachineScheduler machineScheduler,
        IResourceEstimator resourceEstimator,
        IMachineCatalogStore machineCatalogStore,
        IMachineLiveProjectionStore machineLiveProjectionStore,
        IConnectionMultiplexer redis,
        IOptions<InfrastructureOptions> options,
        ILogger<InferenceOrchestrationService> logger)
    {
        _jobQueue = jobQueue;
        _jobStore = jobStore;
        _machineScheduler = machineScheduler;
        _resourceEstimator = resourceEstimator;
        _machineCatalogStore = machineCatalogStore;
        _machineLiveProjectionStore = machineLiveProjectionStore;
        _redis = redis;
        _logger = logger;
        _options = options.Value;
        _coordinator = new CoordinatorActor(this);
    }

    public async Task<IReadOnlyCollection<MachineState>> GetMachinesAsync(CancellationToken cancellationToken)
    {
        return await LoadMachineStatesAsync(cancellationToken, logLivenessTransitions: false);
    }

    public IReadOnlyCollection<WorkerState> GetWorkers()
    {
        return _machines.Values
            .Select(actor => actor.ToWorkerState())
            .OrderBy(x => x.WorkerId, StringComparer.Ordinal)
            .ToArray();
    }

    public int GetPendingJobCount()
    {
        lock (_pendingJobsLock)
        {
            return _pendingJobs.Count + _retryJobs.Count;
        }
    }

    public string GetSchedulerPolicy()
    {
        return _options.WorkerExecution.SchedulerPolicy;
    }

    public void RecordJobAccepted(string model, WeightBand weightBand)
    {
        Interlocked.Increment(ref _totalJobsAccepted);
        IncrementCounter(_acceptedByWeightBand, weightBand.ToString());
    }

    public void RecordJobDeferred(string reason, WeightBand weightBand)
    {
        Interlocked.Increment(ref _totalJobsDeferred);
        Interlocked.Increment(ref _schedulerDeferralCount);
        IncrementCounter(_deferralReasons, reason);
        IncrementCounter(_deferredByWeightBand, weightBand.ToString());
    }

    public async ValueTask<OrchestrationMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var machines = await LoadMachineStatesAsync(cancellationToken, logLivenessTransitions: false);
        var fairShareSnapshot = _coordinator.GetFairShareSnapshot();
        var activeJobs = machines.Sum(x => x.ActiveJobCount);
        var jobCapacity = machines.Sum(x => x.MaxParallelWorkers);
        var reservedVramMb = machines.Sum(x => x.UsedGpuVramMb);
        var totalVramMb = machines.Sum(x => x.GpuVramMb);
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
                SnapshotDictionary(_acceptedByWeightBand),
                SnapshotDictionary(_completedByWeightBand),
                SnapshotDictionary(_deferredByWeightBand),
                SnapshotDictionary(_completedByModel),
                SnapshotDictionary(_failureCategories)),
            new QueueMetricsSnapshot(
                ingressQueueDepth,
                GetPendingJobCount(),
                fairShareSnapshot.BandBufferDepths,
                pendingReasons),
            new SchedulerMetricsSnapshot(
                _options.WorkerExecution.SchedulerPolicy,
                Volatile.Read(ref _schedulerDecisionCount),
                Volatile.Read(ref _schedulerSelectionCount),
                Volatile.Read(ref _schedulerDeferralCount),
                SnapshotDictionary(_dispatchesByWeightBand),
                fairShareSnapshot.BandCredits,
                SnapshotDictionary(_deferralReasons)),
            new BatchMetricsSnapshot(
                totalBatchesFormed,
                totalBatchesFormed == 0 ? 0 : Math.Round((double)batchSizeTotal / totalBatchesFormed, 2),
                recentAverageBatchSize,
                SnapshotDictionary(_batchesByModel)),
            new WorkerUtilizationMetricsSnapshot(
                machines.Count,
                machines.Count(x => x.Status is MachineStatus.Busy or MachineStatus.Saturated),
                machines.Count(x => x.Status == MachineStatus.Saturated),
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
        await InitializeMachinesAsync(stoppingToken);

        _logger.LogInformation(
            "Inference orchestration started. MachineCount: {MachineCount}. SchedulerPolicy: {SchedulerPolicy}. DeferredRetryDelayMs: {DeferredRetryDelayMs}. BatchingEnabled: {BatchingEnabled}. BatchWindowMs: {BatchWindowMs}. MaxBatchSize: {MaxBatchSize}. MaxBatchMemoryMb: {MaxBatchMemoryMb}. ExecutionTimeoutMs: {ExecutionTimeoutMs}. MaxRetries: {MaxRetries}. RetryDelayMs: {RetryDelayMs}. HeartbeatIntervalSeconds: {HeartbeatIntervalSeconds}. HeartbeatTtlSeconds: {HeartbeatTtlSeconds}.",
            _machines.Count,
            _options.WorkerExecution.SchedulerPolicy,
            _options.Scheduling.DeferRetryDelayMs,
            _options.Batching.Enabled,
            _options.Batching.BatchWindowMs,
            _options.Batching.MaxBatchSize,
            _options.Batching.MaxBatchMemoryMb,
            _options.Reliability.ExecutionTimeoutMs,
            _options.Reliability.MaxRetries,
            _options.Reliability.RetryDelayMs,
            _options.WorkerExecution.HeartbeatIntervalSeconds,
            _options.WorkerExecution.HeartbeatTtlSeconds);

        var machineTasks = _machines.Values
            .Select(machine => machine.RunAsync(stoppingToken))
            .ToArray();

        var heartbeatTask = RunHeartbeatLoopAsync(stoppingToken);
        var coordinatorTask = _coordinator.RunAsync(stoppingToken);

        try
        {
            await Task.WhenAll(machineTasks.Append(heartbeatTask).Append(coordinatorTask));
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        finally
        {
            await PublishOfflineAsync(CancellationToken.None);
            _logger.LogInformation("Inference orchestration stopped.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await PublishOfflineAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task InitializeMachinesAsync(CancellationToken cancellationToken)
    {
        var machineCatalog = await _machineCatalogStore.ListAsync(cancellationToken);

        _logger.LogInformation(
            "Loaded durable machine catalog from Postgres. MachineCount: {MachineCount}.",
            machineCatalog.Count);

        foreach (var machine in machineCatalog)
        {
            _machines[machine.MachineId] = new MachineActor(this, machine);
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.WorkerExecution.HeartbeatIntervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var machine in _machines.Values)
            {
                await machine.PublishHeartbeatAsync(cancellationToken);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task<IReadOnlyCollection<MachineState>> LoadMachineStatesAsync(
        CancellationToken cancellationToken,
        bool logLivenessTransitions)
    {
        var catalog = await _machineCatalogStore.ListAsync(cancellationToken);
        var projections = await _machineLiveProjectionStore.GetManyAsync(catalog.Select(x => x.MachineId).ToArray(), cancellationToken);
        var actorSummaries = _machines.Values.ToDictionary(x => x.MachineId, x => x.GetSummary(), StringComparer.Ordinal);
        var machineStates = new List<MachineState>(catalog.Count);

        foreach (var entry in catalog.OrderBy(x => x.MachineId, StringComparer.Ordinal))
        {
            projections.TryGetValue(entry.MachineId, out var projection);
            actorSummaries.TryGetValue(entry.MachineId, out var actorSummary);
            var liveness = ResolveLiveness(entry.Enabled, projection);
            var machineState = MergeMachineState(entry, projection, actorSummary, liveness);
            machineStates.Add(machineState);

            if (logLivenessTransitions)
            {
                LogLivenessTransition(machineState);
            }
        }

        return machineStates;
    }

    private MachineState MergeMachineState(
        MachineCatalogEntry entry,
        MachineLiveProjection? projection,
        MachineActorSummary? actorSummary,
        MachineLivenessState liveness)
    {
        var usedCapacityUnits = projection?.UsedCapacityUnits ?? 0;
        var usedGpuVramMb = projection?.ReservedVramMb ?? 0;
        var activeJobCount = projection?.ActiveJobCount ?? 0;
        var actorStatus = projection?.ActorStatus;
        var runtimeStatus = projection?.RuntimeStatus ?? MachineStatus.Idle;
        var runningJobIds = projection?.RunningJobIds ?? Array.Empty<Guid>();
        var currentModel = projection?.CurrentModel;
        var currentBatchSize = projection?.CurrentBatchSize ?? 0;
        var isAvailable = entry.Enabled && liveness == MachineLivenessState.Live;

        return new MachineState(
            entry.MachineId,
            entry.Name,
            entry.Enabled,
            entry.CreatedAtUtc,
            entry.UpdatedAtUtc,
            entry.TotalCapacityUnits,
            usedCapacityUnits,
            entry.CpuScore,
            entry.RamMb,
            entry.GpuVramMb,
            usedGpuVramMb,
            activeJobCount,
            entry.MaxParallelWorkers,
            projection?.LastHeartbeatUtc,
            runtimeStatus,
            actorStatus,
            liveness,
            isAvailable,
            projection?.ActorInstanceId,
            entry.SupportedModels,
            runningJobIds,
            currentModel,
            currentBatchSize,
            actorSummary?.TotalBatchesFormed ?? 0,
            actorSummary?.LastBatchModel,
            actorSummary?.LastBatchSize ?? 0,
            actorSummary?.LastBatchCompletedUtc,
            actorSummary?.CompletedJobCount ?? 0,
            actorSummary?.FailedJobCount ?? 0);
    }

    private MachineLivenessState ResolveLiveness(bool enabled, MachineLiveProjection? projection)
    {
        if (!enabled)
        {
            return MachineLivenessState.Unavailable;
        }

        if (projection is null)
        {
            return MachineLivenessState.Unavailable;
        }

        if (projection.ActorStatus == MachineActorStatus.Offline)
        {
            return MachineLivenessState.Offline;
        }

        var stalenessThreshold = TimeSpan.FromSeconds(Math.Max(
            _options.WorkerExecution.HeartbeatIntervalSeconds + 1,
            _options.WorkerExecution.HeartbeatTtlSeconds));

        if (DateTime.UtcNow - projection.LastHeartbeatUtc > stalenessThreshold)
        {
            return MachineLivenessState.Stale;
        }

        return MachineLivenessState.Live;
    }

    private void LogLivenessTransition(MachineState machineState)
    {
        var previous = _machineLivenessStates.AddOrUpdate(
            machineState.MachineId,
            machineState.LivenessState,
            (_, oldValue) => oldValue);

        if (previous == machineState.LivenessState)
        {
            return;
        }

        _machineLivenessStates[machineState.MachineId] = machineState.LivenessState;

        _logger.LogInformation(
            "Machine liveness transition detected. MachineId: {MachineId}. Previous: {PreviousLiveness}. Current: {CurrentLiveness}. Enabled: {Enabled}. ActorStatus: {ActorStatus}. LastHeartbeatUtc: {LastHeartbeatUtc}.",
            machineState.MachineId,
            previous,
            machineState.LivenessState,
            machineState.Enabled,
            machineState.ActorStatus,
            machineState.LastHeartbeatUtc);
    }

    private async Task<InferenceJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return await _jobStore.GetAsync(jobId, cancellationToken);
    }

    private void RecordSchedulerDecision(MachineSchedulingDecision decision)
    {
        Interlocked.Increment(ref _schedulerDecisionCount);

        if (decision.SelectedMachine is not null)
        {
            Interlocked.Increment(ref _schedulerSelectionCount);
        }
    }

    private bool ShouldLogDeferred(Guid jobId)
    {
        var now = DateTime.UtcNow;
        var shouldLog = false;

        _deferredLogTimestamps.AddOrUpdate(
            jobId,
            _ =>
            {
                shouldLog = true;
                return now;
            },
            (_, previous) =>
            {
                if (now - previous >= TimeSpan.FromSeconds(5))
                {
                    shouldLog = true;
                    return now;
                }

                return previous;
            });

        return shouldLog;
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

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(50, _options.Reliability.RetryDelayMs)));
                await _jobQueue.EnqueueAsync(job.Id, CancellationToken.None);
                _logger.LogInformation(
                    "Re-enqueued retry job {JobId}. FailureCategory: {FailureCategory}. Attempt: {RetryCount}/{MaxRetries}.",
                    job.Id,
                    category,
                    retryJob.RetryCount,
                    retryJob.MaxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-enqueue retry job {JobId}.", job.Id);
            }
            finally
            {
                _retryJobs.TryRemove(job.Id, out _);
            }
        });
    }

    private async Task PublishOfflineAsync(CancellationToken cancellationToken)
    {
        foreach (var machine in _machines.Values)
        {
            try
            {
                await machine.PublishOfflineAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish offline state for machine {MachineId}.", machine.MachineId);
            }
        }
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
            IncrementCounter(_completedByWeightBand, job.WeightBand.ToString());
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

    private void RecordBandDispatch(WeightBand weightBand)
    {
        IncrementCounter(_dispatchesByWeightBand, weightBand.ToString());
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

    private static IReadOnlyDictionary<string, int> SnapshotDictionary(ConcurrentDictionary<string, int> counters)
    {
        return counters
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CoordinatorActor
    {
        private const int MaxSelectionRounds = 32;

        private readonly InferenceOrchestrationService _owner;
        private readonly SemaphoreSlim _bufferSignal = new(0);
        private readonly object _bandLock = new();
        private readonly WeightBand[] _bandOrder = Enum.GetValues<WeightBand>();
        private readonly Dictionary<WeightBand, Queue<BufferedBandJob>> _bandBuffers;
        private readonly Dictionary<WeightBand, int> _bandCredits;
        private int _nextBandIndex;

        public CoordinatorActor(InferenceOrchestrationService owner)
        {
            _owner = owner;
            _bandBuffers = _bandOrder.ToDictionary(x => x, _ => new Queue<BufferedBandJob>());
            _bandCredits = _bandOrder.ToDictionary(x => x, _ => 0);
        }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(
                RunIngressLoopAsync(cancellationToken),
                RunReconciliationLoopAsync(cancellationToken),
                RunAssignmentLoopAsync(cancellationToken));
        }

        public FairShareSnapshot GetFairShareSnapshot()
        {
            lock (_bandLock)
            {
                return new FairShareSnapshot(
                    _bandOrder.ToDictionary(
                        band => band.ToString(),
                        band => _bandBuffers[band].Count,
                        StringComparer.OrdinalIgnoreCase),
                    _bandOrder.ToDictionary(
                        band => band.ToString(),
                        band => _bandCredits[band],
                        StringComparer.OrdinalIgnoreCase));
            }
        }

        private async Task RunIngressLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var jobId = await _owner._jobQueue.DequeueAsync(cancellationToken);
                    _owner._logger.LogInformation("CoordinatorActor dequeued job {JobId} from ingress queue.", jobId);
                    await BufferJobAsync(jobId, "IngressQueue", cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunReconciliationLoopAsync(CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Max(50, _owner._options.Scheduling.DeferRetryDelayMs));

            while (!cancellationToken.IsCancellationRequested)
            {
                while (_owner.TryTakeReadyDeferred(out var deferred))
                {
                    _owner._logger.LogDebug(
                        "CoordinatorActor requeued deferred job {JobId}. PreviousReason: {Reason}.",
                        deferred.JobId,
                        deferred.Reason);

                    await BufferJobAsync(deferred.JobId, "DeferredReconciliation", cancellationToken);
                }

                await Task.Delay(interval, cancellationToken);
            }
        }

        private async Task RunAssignmentLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TrySelectNextBufferedJob(out var bufferedJob, out var creditBeforeDebit, out var creditAfterDebit))
                {
                    if (GetPendingBufferedCount() == 0)
                    {
                        await _bufferSignal.WaitAsync(cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(10, cancellationToken);
                    }

                    continue;
                }

                var job = await _owner.GetJobAsync(bufferedJob.JobId, cancellationToken);
                if (job is null)
                {
                    _owner._logger.LogWarning(
                        "CoordinatorActor received job {JobId}, but durable state was not found.",
                        bufferedJob.JobId);
                    continue;
                }

                var estimate = _owner._resourceEstimator.Estimate(job);
                var machineStates = await _owner.LoadMachineStatesAsync(cancellationToken, logLivenessTransitions: true);
                var decision = _owner._machineScheduler.Evaluate(job, estimate, machineStates);
                _owner.RecordSchedulerDecision(decision);

                var excludedByLiveness = decision.Evaluations
                    .Where(x => x.Reason is "MachineOffline" or "MachineStale" or "MachineUnavailable" or "MachineDisabled")
                    .ToArray();

                if (excludedByLiveness.Length > 0)
                {
                    _owner._logger.LogDebug(
                        "CoordinatorActor excluded machines for job {JobId} due to availability/liveness: {ExcludedMachines}.",
                        job.Id,
                        string.Join(", ", excludedByLiveness.Select(x => $"{x.MachineId}={x.Reason}")));
                }

                _owner._logger.LogInformation(
                    "CoordinatorActor selected band {WeightBand} for job {JobId}. ExactWeightDebit: {ExactWeightDebit}. CreditBeforeDebit: {CreditBeforeDebit}. CreditAfterDebit: {CreditAfterDebit}. BufferedJobsRemainingInBand: {BufferedJobsRemainingInBand}.",
                    bufferedJob.WeightBand,
                    job.Id,
                    job.Weight,
                    creditBeforeDebit,
                    creditAfterDebit,
                    GetBufferedCount(bufferedJob.WeightBand));

                _owner._logger.LogDebug(
                    "CoordinatorActor evaluated job {JobId}. Estimate: {EffectiveCostUnits} units ({WeightBand}). Evaluations: {Evaluations}",
                    job.Id,
                    estimate.EffectiveCostUnits,
                    estimate.WeightBand,
                    string.Join(", ", decision.Evaluations.Select(x => $"{x.MachineId}={x.Reason}[cap:{x.RemainingCapacityUnits},vram:{x.RemainingGpuVramMb},slots:{x.AvailableWorkerSlots}]")));

                if (decision.SelectedMachine is null)
                {
                    _owner.DeferJob(job.Id, "NoEligibleMachine");
                    _owner.RecordJobDeferred("NoEligibleMachine", job.WeightBand);

                    if (_owner.ShouldLogDeferred(job.Id))
                    {
                        _owner._logger.LogWarning(
                            "CoordinatorActor deferred job {JobId}. WeightBand: {WeightBand}. Model: {Model}. RequiredMemoryMb: {RequiredMemoryMb}. EffectiveCostUnits: {EffectiveCostUnits}. PendingJobs: {PendingJobs}.",
                            job.Id,
                            job.WeightBand,
                            job.Model,
                            job.RequiredMemoryMb,
                            estimate.EffectiveCostUnits,
                            _owner.GetPendingJobCount());
                    }

                    continue;
                }

                if (!_owner._machines.TryGetValue(decision.SelectedMachine.MachineId, out var machine))
                {
                    _owner.DeferJob(job.Id, "SelectedMachineMissing");
                    _owner.RecordJobDeferred("SelectedMachineMissing", job.WeightBand);
                    continue;
                }

                if (!machine.TryAccept(job, estimate, out var rejectionReason))
                {
                    var reason = rejectionReason ?? "ReservationRejected";
                    _owner.DeferJob(job.Id, reason);
                    _owner.RecordJobDeferred(reason, job.WeightBand);

                    if (_owner.ShouldLogDeferred(job.Id))
                    {
                        _owner._logger.LogWarning(
                            "CoordinatorActor deferred job {JobId}. WeightBand: {WeightBand}. Machine {MachineId} rejected assignment. Reason: {Reason}. PendingJobs: {PendingJobs}.",
                            job.Id,
                            job.WeightBand,
                            machine.MachineId,
                            reason,
                            _owner.GetPendingJobCount());
                    }

                    continue;
                }

                await machine.EnqueueAsync(new MachineAssignment(job.Id, estimate), cancellationToken);

                _owner._logger.LogInformation(
                    "CoordinatorActor assigned job {JobId} to machine {MachineId}. EffectiveCostUnits: {EffectiveCostUnits}. WeightBand: {WeightBand}. MachineCapacity: {UsedCapacityUnits}/{TotalCapacityUnits}. ActiveJobs: {ActiveJobCount}/{MaxParallelWorkers}.",
                    job.Id,
                    machine.MachineId,
                    estimate.EffectiveCostUnits,
                    estimate.WeightBand,
                    machine.UsedCapacityUnits,
                    machine.TotalCapacityUnits,
                    machine.ActiveJobCount,
                    machine.MaxParallelWorkers);

                _owner.RecordBandDispatch(bufferedJob.WeightBand);
            }
        }

        private async Task BufferJobAsync(Guid jobId, string source, CancellationToken cancellationToken)
        {
            var job = await _owner.GetJobAsync(jobId, cancellationToken);
            if (job is null)
            {
                _owner._logger.LogWarning(
                    "CoordinatorActor could not buffer job {JobId} from {Source} because durable state was missing.",
                    jobId,
                    source);
                return;
            }

            lock (_bandLock)
            {
                _bandBuffers[job.WeightBand].Enqueue(new BufferedBandJob(job.Id, job.WeightBand, job.Weight));
            }

            _bufferSignal.Release();

            _owner._logger.LogDebug(
                "CoordinatorActor buffered job {JobId} into band {WeightBand} from {Source}. ExactWeight: {ExactWeight}. BandDepth: {BandDepth}.",
                job.Id,
                job.WeightBand,
                source,
                job.Weight,
                GetBufferedCount(job.WeightBand));
        }

        private bool TrySelectNextBufferedJob(
            out BufferedBandJob bufferedJob,
            out int creditBeforeDebit,
            out int creditAfterDebit)
        {
            lock (_bandLock)
            {
                bufferedJob = default;
                creditBeforeDebit = 0;
                creditAfterDebit = 0;

                if (_bandBuffers.Values.All(queue => queue.Count == 0))
                {
                    return false;
                }

                for (var round = 0; round < MaxSelectionRounds; round++)
                {
                    for (var i = 0; i < _bandOrder.Length; i++)
                    {
                        var band = _bandOrder[_nextBandIndex];
                        _nextBandIndex = (_nextBandIndex + 1) % _bandOrder.Length;

                        var queue = _bandBuffers[band];
                        if (queue.Count == 0)
                        {
                            continue;
                        }

                        _bandCredits[band] += GetQuantum(band);
                        var candidate = queue.Peek();

                        if (_bandCredits[band] < candidate.ExactWeight)
                        {
                            continue;
                        }

                        queue.Dequeue();
                        creditBeforeDebit = _bandCredits[band];
                        _bandCredits[band] -= candidate.ExactWeight;
                        creditAfterDebit = _bandCredits[band];
                        bufferedJob = candidate;
                        return true;
                    }
                }

                return false;
            }
        }

        private int GetPendingBufferedCount()
        {
            lock (_bandLock)
            {
                return _bandBuffers.Values.Sum(queue => queue.Count);
            }
        }

        private int GetBufferedCount(WeightBand band)
        {
            lock (_bandLock)
            {
                return _bandBuffers[band].Count;
            }
        }

        private static int GetQuantum(WeightBand band)
        {
            return band switch
            {
                WeightBand.W1_2 => 2,
                WeightBand.W3_5 => 5,
                WeightBand.W6_10 => 10,
                WeightBand.W11_20 => 20,
                WeightBand.W21_40 => 40,
                WeightBand.W41Plus => 60,
                _ => 10
            };
        }

        private readonly record struct BufferedBandJob(Guid JobId, WeightBand WeightBand, int ExactWeight);
    }

    private sealed class MachineActor
    {
        private readonly InferenceOrchestrationService _owner;
        private readonly object _sync = new();
        private readonly Channel<MachineAssignment> _mailbox = Channel.CreateUnbounded<MachineAssignment>();
        private readonly Dictionary<Guid, ReservedJob> _reservedJobs = new();
        private readonly string[] _supportedModels;
        private readonly string _actorInstanceId = $"actor-{Guid.NewGuid():N}";
        private readonly string _machineName;
        private readonly bool _enabled;
        private DateTime _lastHeartbeatUtc = DateTime.UtcNow;
        private int _currentBatchSize;
        private int _totalBatchesFormed;
        private string? _lastBatchModel;
        private int _lastBatchSize;
        private DateTime? _lastBatchCompletedUtc;
        private int _completedJobCount;
        private int _failedJobCount;
        private readonly Dictionary<string, int> _executingModels = new(StringComparer.OrdinalIgnoreCase);
        private MachineActorStatus _actorStatus = MachineActorStatus.Starting;

        public MachineActor(InferenceOrchestrationService owner, MachineCatalogEntry catalogEntry)
        {
            _owner = owner;
            MachineId = catalogEntry.MachineId;
            _machineName = catalogEntry.Name;
            _enabled = catalogEntry.Enabled;
            TotalCapacityUnits = catalogEntry.TotalCapacityUnits;
            CpuScore = catalogEntry.CpuScore;
            RamMb = catalogEntry.RamMb;
            GpuVramMb = catalogEntry.GpuVramMb;
            MaxParallelWorkers = catalogEntry.MaxParallelWorkers;
            _supportedModels = catalogEntry.SupportedModels
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string MachineId { get; }
        public int TotalCapacityUnits { get; }
        public int CpuScore { get; }
        public int RamMb { get; }
        public int GpuVramMb { get; }
        public int MaxParallelWorkers { get; }

        public int ActiveJobCount
        {
            get
            {
                lock (_sync)
                {
                    return _reservedJobs.Count;
                }
            }
        }

        public int UsedCapacityUnits
        {
            get
            {
                lock (_sync)
                {
                    return _reservedJobs.Values.Sum(x => x.CostUnits);
                }
            }
        }

        public Task EnqueueAsync(MachineAssignment assignment, CancellationToken cancellationToken)
        {
            return _mailbox.Writer.WriteAsync(assignment, cancellationToken).AsTask();
        }

        public bool TryAccept(InferenceJob job, JobResourceEstimate estimate, out string? rejectionReason)
        {
            lock (_sync)
            {
                if (!_enabled)
                {
                    rejectionReason = "MachineDisabled";
                    return false;
                }

                if (job.Model is null)
                {
                    rejectionReason = "JobModelNotResolved";
                    return false;
                }

                if (!_supportedModels.Contains(job.Model, StringComparer.OrdinalIgnoreCase))
                {
                    rejectionReason = "ModelNotSupported";
                    return false;
                }

                if (_reservedJobs.Count >= MaxParallelWorkers)
                {
                    rejectionReason = "NoExecutionSlot";
                    return false;
                }

                var usedCapacityUnits = _reservedJobs.Values.Sum(x => x.CostUnits);
                if (TotalCapacityUnits - usedCapacityUnits < estimate.EffectiveCostUnits)
                {
                    rejectionReason = "InsufficientCapacityUnits";
                    return false;
                }

                var usedGpuVramMb = _reservedJobs.Values.Sum(x => x.RequiredMemoryMb);
                if (GpuVramMb - usedGpuVramMb < job.RequiredMemoryMb)
                {
                    rejectionReason = "InsufficientGpuVram";
                    return false;
                }

                _reservedJobs[job.Id] = new ReservedJob(job.Id, job.Model, job.RequiredMemoryMb, estimate.EffectiveCostUnits);
                _lastHeartbeatUtc = DateTime.UtcNow;
                rejectionReason = null;
            }

            QueueProjectionRefresh();
            return true;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            SetActorStatus(MachineActorStatus.Online);
            await PublishProjectionAsync(cancellationToken);

            var loops = Enumerable.Range(0, Math.Max(1, MaxParallelWorkers))
                .Select(_ => RunExecutionLoopAsync(cancellationToken))
                .ToArray();

            await Task.WhenAll(loops);
        }

        public async Task PublishHeartbeatAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
            }

            await PublishProjectionAsync(cancellationToken);
        }

        public async Task PublishOfflineAsync(CancellationToken cancellationToken)
        {
            SetActorStatus(MachineActorStatus.Offline);
            lock (_sync)
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
            }

            await _owner._machineLiveProjectionStore.PublishOfflineAsync(
                BuildLiveProjection(),
                _owner.GetHeartbeatTtl(),
                cancellationToken);
        }

        public WorkerState ToWorkerState()
        {
            lock (_sync)
            {
                var reservedJobs = _reservedJobs.Values.ToArray();
                var usedGpuVramMb = reservedJobs.Sum(x => x.RequiredMemoryMb);
                var currentModel = _executingModels.Count switch
                {
                    0 => null,
                    1 => _executingModels.Keys.First(),
                    _ => "mixed"
                };

                var status = reservedJobs.Length == 0
                    ? WorkerStatus.Idle
                    : reservedJobs.Length >= MaxParallelWorkers || usedGpuVramMb >= GpuVramMb
                        ? WorkerStatus.Saturated
                        : WorkerStatus.Busy;

                return new WorkerState(
                    MachineId,
                    _machineName,
                    reservedJobs.Length,
                    MaxParallelWorkers,
                    _lastHeartbeatUtc,
                    status,
                    $"{MachineId}-gpu",
                    GpuVramMb,
                    usedGpuVramMb,
                    _supportedModels,
                    reservedJobs.Select(x => x.JobId).OrderBy(x => x).ToArray(),
                    currentModel,
                    _currentBatchSize,
                    _totalBatchesFormed,
                    _lastBatchModel,
                    _lastBatchSize,
                    _lastBatchCompletedUtc,
                    _completedJobCount,
                    _failedJobCount);
            }
        }

        public MachineActorSummary GetSummary()
        {
            lock (_sync)
            {
                return new MachineActorSummary(
                    _totalBatchesFormed,
                    _lastBatchModel,
                    _lastBatchSize,
                    _lastBatchCompletedUtc,
                    _completedJobCount,
                    _failedJobCount);
            }
        }

        private async Task RunExecutionLoopAsync(CancellationToken cancellationToken)
        {
            var reader = _mailbox.Reader;
            var buffered = new Queue<MachineAssignment>();

            while (!cancellationToken.IsCancellationRequested)
            {
                MachineAssignment assignment;

                if (buffered.Count > 0)
                {
                    assignment = buffered.Dequeue();
                }
                else
                {
                    try
                    {
                        assignment = await reader.ReadAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                await ProcessBatchAsync(reader, assignment, buffered, cancellationToken);
            }
        }

        private async Task ProcessBatchAsync(
            ChannelReader<MachineAssignment> reader,
            MachineAssignment firstAssignment,
            Queue<MachineAssignment> bufferedAssignments,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
            }

            var batchJobs = new List<InferenceJob>();
            var batchAssignments = new List<MachineAssignment>();
            var batchModel = string.Empty;
            var batchMemoryMb = 0;

            try
            {
                var firstJob = await _owner.GetJobAsync(firstAssignment.JobId, cancellationToken);
                if (firstJob is null)
                {
                    _owner._logger.LogWarning(
                        "MachineActor {MachineId} received job {JobId} but durable state no longer exists.",
                        MachineId,
                        firstAssignment.JobId);
                    ReleaseReservation(firstAssignment.JobId);
                    return;
                }

                batchJobs.Add(firstJob);
                batchAssignments.Add(firstAssignment);
                batchModel = firstJob.Model ?? string.Empty;
                batchMemoryMb = firstJob.RequiredMemoryMb;

                var batchWindow = _owner._options.Batching.Enabled
                    ? TimeSpan.FromMilliseconds(Math.Max(0, _owner._options.Batching.BatchWindowMs))
                    : TimeSpan.Zero;
                var deadlineUtc = DateTime.UtcNow.Add(batchWindow);

                while (_owner._options.Batching.Enabled && batchAssignments.Count < Math.Max(1, _owner._options.Batching.MaxBatchSize))
                {
                    if (DateTime.UtcNow >= deadlineUtc)
                    {
                        break;
                    }

                    var nextAssignment = await TryReadNextCandidateAsync(reader, bufferedAssignments, deadlineUtc, cancellationToken);
                    if (nextAssignment is null)
                    {
                        break;
                    }

                    var nextJob = await _owner.GetJobAsync(nextAssignment.Value.JobId, cancellationToken);
                    if (nextJob is null)
                    {
                        _owner._logger.LogWarning(
                            "MachineActor {MachineId} skipped missing job {JobId} during batch assembly.",
                            MachineId,
                            nextAssignment.Value.JobId);
                        ReleaseReservation(nextAssignment.Value.JobId);
                        continue;
                    }

                    if (!CanJoinBatch(batchModel, batchMemoryMb, nextJob))
                    {
                        bufferedAssignments.Enqueue(nextAssignment.Value);
                        break;
                    }

                    batchJobs.Add(nextJob);
                    batchAssignments.Add(nextAssignment.Value);
                    batchMemoryMb += nextJob.RequiredMemoryMb;
                }

                StartBatch(batchJobs);
                _owner.RecordBatchFormed(batchModel, batchJobs.Count);

                _owner._logger.LogInformation(
                    "MachineActor {MachineId} formed batch. BatchSize: {BatchSize}. Model: {Model}. BatchMemoryMb: {BatchMemoryMb}. CapacityUnits: {UsedCapacityUnits}/{TotalCapacityUnits}. GpuVramMb: {UsedGpuVramMb}/{GpuVramMb}.",
                    MachineId,
                    batchJobs.Count,
                    batchModel,
                    batchMemoryMb,
                    UsedCapacityUnits,
                    TotalCapacityUnits,
                    ToWorkerState().VramReservedMb,
                    GpuVramMb);

                var startedAtUtc = DateTime.UtcNow;
                foreach (var job in batchJobs)
                {
                    job.MarkProcessing(startedAtUtc);
                    await _owner._jobStore.UpdateAsync(job, cancellationToken);
                }

                using var executionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                executionSource.CancelAfter(Math.Max(250, _owner._options.Reliability.ExecutionTimeoutMs));

                await SimulateBatchExecutionAsync(batchJobs, executionSource.Token);

                foreach (var job in batchJobs)
                {
                    job.MarkCompleted($"Simulated response for prompt: {job.Prompt}", DateTime.UtcNow);
                    await _owner._jobStore.UpdateAsync(job, cancellationToken);
                    MarkJobCompleted();
                    _owner.RecordJobFinalized(job, job.Model ?? "unknown", succeeded: true, failureCategory: null, terminal: false);

                    var latency = BuildLatencySample(job);
                    _owner._logger.LogInformation(
                        "MachineActor {MachineId} completed job {JobId}. BatchSize: {BatchSize}. QueueWaitMs: {QueueWaitMs}. ExecutionMs: {ExecutionMs}. TotalLatencyMs: {TotalLatencyMs}.",
                        MachineId,
                        job.Id,
                        batchJobs.Count,
                        latency.QueueWaitMs,
                        latency.ExecutionMs,
                        latency.TotalLatencyMs);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await HandleBatchFailureAsync(
                    batchJobs,
                    JobFailureCategory.Timeout,
                    "Execution timed out before the batch completed.",
                    CancellationToken.None);
            }
            catch (NonRetryableExecutionException ex)
            {
                await HandleBatchFailureAsync(
                    batchJobs,
                    JobFailureCategory.NonRetryableError,
                    ex.Message,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                await HandleBatchFailureAsync(
                    batchJobs,
                    JobFailureCategory.ExecutionError,
                    ex.Message,
                    CancellationToken.None);
            }
            finally
            {
                CompleteBatch(batchJobs);

                foreach (var assignment in batchAssignments)
                {
                    ReleaseReservation(assignment.JobId);
                }

                _owner._logger.LogInformation(
                    "MachineActor {MachineId} released resources. BatchSize: {BatchSize}. ActiveJobs: {ActiveJobCount}/{MaxParallelWorkers}. CapacityUnits: {UsedCapacityUnits}/{TotalCapacityUnits}. GpuVramMb: {UsedGpuVramMb}/{GpuVramMb}. TotalBatchesFormed: {TotalBatchesFormed}.",
                    MachineId,
                    batchJobs.Count,
                    ActiveJobCount,
                    MaxParallelWorkers,
                    UsedCapacityUnits,
                    TotalCapacityUnits,
                    ToWorkerState().VramReservedMb,
                    GpuVramMb,
                    GetSummary().TotalBatchesFormed);
            }
        }

        private async Task HandleBatchFailureAsync(
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
                    Interlocked.Increment(ref _owner._totalJobsTimedOut);
                }

                if (IsRetryable(category) && job.CanRetry())
                {
                    job.MarkRetrying(failureReason, category, timestampUtc);
                    await _owner._jobStore.UpdateAsync(job, cancellationToken);
                    _owner.EnqueueRetry(job, category);
                    _owner.RecordJobRetried(category);

                    _owner._logger.LogWarning(
                        "MachineActor {MachineId} scheduled retry for job {JobId}. FailureCategory: {FailureCategory}. Attempt: {RetryCount}/{MaxRetries}. RetryDelayMs: {RetryDelayMs}.",
                        MachineId,
                        job.Id,
                        category,
                        job.RetryCount,
                        job.MaxRetries,
                        _owner._options.Reliability.RetryDelayMs);

                    continue;
                }

                var terminalReason = IsRetryable(category) && !job.CanRetry()
                    ? $"Retry exhausted after {category}: {failureReason}"
                    : failureReason;

                var terminalCategory = IsRetryable(category) && !job.CanRetry()
                    ? JobFailureCategory.RetryExhausted
                    : category;

                job.MarkDeadLettered(terminalReason, terminalCategory, timestampUtc);
                await _owner._jobStore.UpdateAsync(job, cancellationToken);
                MarkJobFailed();
                _owner.RecordJobFinalized(job, job.Model ?? "unknown", succeeded: false, failureCategory: terminalCategory, terminal: true);

                if (terminalCategory == JobFailureCategory.RetryExhausted)
                {
                    Interlocked.Increment(ref _owner._retryExhaustedCount);
                }

                _owner._logger.LogError(
                    "MachineActor {MachineId} moved job {JobId} to dead letter. FailureCategory: {FailureCategory}. RetryCount: {RetryCount}/{MaxRetries}.",
                    MachineId,
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
                delayMs = Math.Max(delayMs, _owner._options.Reliability.ExecutionTimeoutMs + 500);
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        private async Task<MachineAssignment?> TryReadNextCandidateAsync(
            ChannelReader<MachineAssignment> reader,
            Queue<MachineAssignment> bufferedAssignments,
            DateTime deadlineUtc,
            CancellationToken cancellationToken)
        {
            if (bufferedAssignments.Count > 0)
            {
                return bufferedAssignments.Dequeue();
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

            var maxBatchMemoryMb = Math.Max(1, _owner._options.Batching.MaxBatchMemoryMb);
            return batchMemoryMb + nextJob.RequiredMemoryMb <= maxBatchMemoryMb;
        }

        private void StartBatch(IReadOnlyCollection<InferenceJob> batch)
        {
            lock (_sync)
            {
                _currentBatchSize += batch.Count;
                _totalBatchesFormed++;
                _lastBatchModel = batch.FirstOrDefault()?.Model;
                _lastBatchSize = batch.Count;

                foreach (var job in batch)
                {
                    if (job.Model is null)
                    {
                        continue;
                    }

                    _executingModels.TryGetValue(job.Model, out var current);
                    _executingModels[job.Model] = current + 1;
                }

                _lastHeartbeatUtc = DateTime.UtcNow;
            }

            QueueProjectionRefresh();
        }

        private void CompleteBatch(IReadOnlyCollection<InferenceJob> batch)
        {
            lock (_sync)
            {
                _currentBatchSize = Math.Max(0, _currentBatchSize - batch.Count);
                _lastBatchCompletedUtc = DateTime.UtcNow;
                _lastHeartbeatUtc = DateTime.UtcNow;

                foreach (var job in batch)
                {
                    if (job.Model is null || !_executingModels.TryGetValue(job.Model, out var current))
                    {
                        continue;
                    }

                    if (current <= 1)
                    {
                        _executingModels.Remove(job.Model);
                    }
                    else
                    {
                        _executingModels[job.Model] = current - 1;
                    }
                }
            }

            QueueProjectionRefresh();
        }

        private void ReleaseReservation(Guid jobId)
        {
            lock (_sync)
            {
                _reservedJobs.Remove(jobId);
                _lastHeartbeatUtc = DateTime.UtcNow;
            }

            QueueProjectionRefresh();
        }

        private void MarkJobCompleted()
        {
            lock (_sync)
            {
                _completedJobCount++;
            }
        }

        private void MarkJobFailed()
        {
            lock (_sync)
            {
                _failedJobCount++;
            }
        }

        private void SetActorStatus(MachineActorStatus actorStatus)
        {
            lock (_sync)
            {
                _actorStatus = actorStatus;
            }
        }

        private MachineLiveProjection BuildLiveProjection()
        {
            lock (_sync)
            {
                var reservedJobs = _reservedJobs.Values.ToArray();
                var usedCapacityUnits = reservedJobs.Sum(x => x.CostUnits);
                var reservedVramMb = reservedJobs.Sum(x => x.RequiredMemoryMb);
                var currentModel = _executingModels.Count switch
                {
                    0 => null,
                    1 => _executingModels.Keys.First(),
                    _ => "mixed"
                };

                var runtimeStatus = reservedJobs.Length == 0
                    ? MachineStatus.Idle
                    : reservedJobs.Length >= MaxParallelWorkers || usedCapacityUnits >= TotalCapacityUnits || reservedVramMb >= GpuVramMb
                        ? MachineStatus.Saturated
                        : MachineStatus.Busy;

                return new MachineLiveProjection(
                    MachineId,
                    _actorInstanceId,
                    _actorStatus,
                    _lastHeartbeatUtc,
                    usedCapacityUnits,
                    Math.Max(0, TotalCapacityUnits - usedCapacityUnits),
                    reservedJobs.Length,
                    reservedVramMb,
                    reservedJobs.Select(x => x.JobId).OrderBy(x => x).ToArray(),
                    _currentBatchSize,
                    currentModel,
                    runtimeStatus);
            }
        }

        private async Task PublishProjectionAsync(CancellationToken cancellationToken)
        {
            var projection = BuildLiveProjection();
            await _owner._machineLiveProjectionStore.PublishAsync(projection, _owner.GetHeartbeatTtl(), cancellationToken);

            _owner._logger.LogDebug(
                "Published machine heartbeat projection. MachineId: {MachineId}. ActorStatus: {ActorStatus}. ActiveJobCount: {ActiveJobCount}. UsedCapacityUnits: {UsedCapacityUnits}. ReservedVramMb: {ReservedVramMb}.",
                projection.MachineId,
                projection.ActorStatus,
                projection.ActiveJobCount,
                projection.UsedCapacityUnits,
                projection.ReservedVramMb);
        }

        private void QueueProjectionRefresh()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishProjectionAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _owner._logger.LogWarning(ex, "Failed to refresh live projection for machine {MachineId}.", MachineId);
                }
            });
        }
    }

    private TimeSpan GetHeartbeatTtl()
    {
        return TimeSpan.FromSeconds(Math.Max(
            _options.WorkerExecution.HeartbeatIntervalSeconds + 1,
            _options.WorkerExecution.HeartbeatTtlSeconds));
    }

    private readonly record struct DeferredJob(Guid JobId, DateTime RetryAfterUtc, string Reason);
    private readonly record struct RetryJob(Guid JobId, DateTime RetryAfterUtc, JobFailureCategory FailureCategory, int RetryCount, int MaxRetries);
    private readonly record struct LatencySample(long QueueWaitMs, long ExecutionMs, long TotalLatencyMs);
    private readonly record struct MachineAssignment(Guid JobId, JobResourceEstimate Estimate);
    private readonly record struct ReservedJob(Guid JobId, string? Model, int RequiredMemoryMb, int CostUnits);
    private readonly record struct FairShareSnapshot(
        IReadOnlyDictionary<string, int> BandBufferDepths,
        IReadOnlyDictionary<string, int> BandCredits);
    private readonly record struct MachineActorSummary(
        int TotalBatchesFormed,
        string? LastBatchModel,
        int LastBatchSize,
        DateTime? LastBatchCompletedUtc,
        int CompletedJobCount,
        int FailedJobCount);

    private sealed class NonRetryableExecutionException : Exception
    {
        public NonRetryableExecutionException(string message)
            : base(message)
        {
        }
    }
}
