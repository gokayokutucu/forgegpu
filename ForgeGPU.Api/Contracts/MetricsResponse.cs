namespace ForgeGPU.Api.Contracts;

public sealed record MetricsResponse(
    DateTime GeneratedAtUtc,
    JobMetricsResponse Jobs,
    QueueMetricsResponse Queue,
    SchedulerMetricsResponse Scheduler,
    BatchMetricsResponse Batching,
    WorkerUtilizationMetricsResponse Workers,
    LatencyMetricsResponse Latency);

public sealed record JobMetricsResponse(
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
    IReadOnlyDictionary<string, int> AcceptedByWeightBand,
    IReadOnlyDictionary<string, int> CompletedByWeightBand,
    IReadOnlyDictionary<string, int> DeferredByWeightBand,
    IReadOnlyDictionary<string, int> CompletedByModel,
    IReadOnlyDictionary<string, int> FailuresByCategory);

public sealed record QueueMetricsResponse(
    long? IngressQueueDepth,
    int DeferredPendingCount,
    IReadOnlyDictionary<string, int> IngressPublishedByTopic,
    IReadOnlyDictionary<string, int> IngressConsumedByWeightBand,
    IReadOnlyDictionary<string, int> IngressConsumedByTopic,
    IReadOnlyDictionary<string, int> IngressLagByTopic,
    IReadOnlyDictionary<string, int> CurrentBandBufferDepths,
    IReadOnlyDictionary<string, int> PendingReasons);

public sealed record SchedulerMetricsResponse(
    string Policy,
    long TotalDecisions,
    long SuccessfulDispatches,
    long Deferrals,
    IReadOnlyDictionary<string, int> DispatchesByWeightBand,
    IReadOnlyDictionary<string, int> BandCredits,
    IReadOnlyDictionary<string, int> DeferralReasons);

public sealed record BatchMetricsResponse(
    long TotalBatchesFormed,
    double AverageBatchSize,
    double RecentAverageBatchSize,
    IReadOnlyDictionary<string, int> BatchesByModel);

public sealed record WorkerUtilizationMetricsResponse(
    int TotalWorkers,
    int BusyWorkers,
    int SaturatedWorkers,
    int ActiveJobs,
    int JobCapacity,
    double JobUtilizationPercent,
    int ReservedVramMb,
    int TotalVramMb,
    double VramUtilizationPercent);

public sealed record LatencyMetricsResponse(
    long Samples,
    double AverageQueueWaitMs,
    double AverageExecutionMs,
    double AverageTotalLatencyMs,
    double RecentAverageQueueWaitMs,
    double RecentAverageExecutionMs,
    double RecentAverageTotalLatencyMs);
