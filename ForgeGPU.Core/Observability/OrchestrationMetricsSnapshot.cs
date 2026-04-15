namespace ForgeGPU.Core.Observability;

public sealed record OrchestrationMetricsSnapshot(
    DateTime GeneratedAtUtc,
    JobMetricsSnapshot Jobs,
    QueueMetricsSnapshot Queue,
    SchedulerMetricsSnapshot Scheduler,
    BatchMetricsSnapshot Batching,
    WorkerUtilizationMetricsSnapshot Workers,
    LatencyMetricsSnapshot Latency);

public sealed record JobMetricsSnapshot(
    long TotalAccepted,
    long TotalCompleted,
    long TotalFailed,
    long TotalRetried,
    long TotalTimedOut,
    long TotalTerminalFailures,
    long RetryExhaustedCount,
    long DeadLetterCount,
    long TotalDeferred,
    int CurrentProcessing,
    IReadOnlyDictionary<string, int> CompletedByModel,
    IReadOnlyDictionary<string, int> FailuresByCategory);

public sealed record QueueMetricsSnapshot(
    long? IngressQueueDepth,
    int DeferredPendingCount,
    IReadOnlyDictionary<string, int> PendingReasons);

public sealed record SchedulerMetricsSnapshot(
    string Policy,
    long TotalDecisions,
    long SuccessfulDispatches,
    long Deferrals,
    IReadOnlyDictionary<string, int> DeferralReasons);

public sealed record BatchMetricsSnapshot(
    long TotalBatchesFormed,
    double AverageBatchSize,
    double RecentAverageBatchSize,
    IReadOnlyDictionary<string, int> BatchesByModel);

public sealed record WorkerUtilizationMetricsSnapshot(
    int TotalWorkers,
    int BusyWorkers,
    int SaturatedWorkers,
    int ActiveJobs,
    int JobCapacity,
    double JobUtilizationPercent,
    int ReservedVramMb,
    int TotalVramMb,
    double VramUtilizationPercent);

public sealed record LatencyMetricsSnapshot(
    long Samples,
    double AverageQueueWaitMs,
    double AverageExecutionMs,
    double AverageTotalLatencyMs,
    double RecentAverageQueueWaitMs,
    double RecentAverageExecutionMs,
    double RecentAverageTotalLatencyMs);
