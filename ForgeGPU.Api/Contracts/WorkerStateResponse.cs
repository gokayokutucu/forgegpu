using ForgeGPU.Core.InferenceWorkers;

namespace ForgeGPU.Api.Contracts;

public sealed record WorkerStateResponse(
    string WorkerId,
    string Name,
    int ActiveJobCount,
    int MaxConcurrentJobs,
    DateTime LastHeartbeatUtc,
    WorkerStatus Status,
    string GpuId,
    int VramTotalMb,
    int VramReservedMb,
    int VramAvailableMb,
    IReadOnlyCollection<string> SupportedModels,
    IReadOnlyCollection<Guid> AssignedJobIds,
    string? CurrentModel,
    int CurrentBatchSize,
    int TotalBatchesFormed,
    string? LastBatchModel,
    int LastBatchSize,
    DateTime? LastBatchCompletedUtc,
    int CompletedJobCount,
    int FailedJobCount,
    double JobUtilizationPercent,
    double VramUtilizationPercent);

public sealed record WorkersSnapshotResponse(
    string SchedulerPolicy,
    int PendingJobCount,
    IReadOnlyCollection<WorkerStateResponse> Workers);
