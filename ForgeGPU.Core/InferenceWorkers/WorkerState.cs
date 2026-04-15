namespace ForgeGPU.Core.InferenceWorkers;

public sealed record WorkerState(
    string WorkerId,
    string Name,
    int ActiveJobCount,
    int MaxConcurrentJobs,
    DateTime LastHeartbeatUtc,
    WorkerStatus Status,
    string GpuId,
    int VramTotalMb,
    int VramReservedMb,
    IReadOnlyCollection<string> SupportedModels,
    IReadOnlyCollection<Guid> AssignedJobIds,
    string? CurrentModel,
    int CurrentBatchSize,
    int TotalBatchesFormed,
    string? LastBatchModel,
    int LastBatchSize,
    DateTime? LastBatchCompletedUtc,
    int CompletedJobCount,
    int FailedJobCount)
{
    public int VramAvailableMb => Math.Max(0, VramTotalMb - VramReservedMb);
    public double JobUtilizationPercent => MaxConcurrentJobs == 0
        ? 0
        : Math.Round((double)ActiveJobCount / MaxConcurrentJobs * 100, 2);
    public double VramUtilizationPercent => VramTotalMb == 0
        ? 0
        : Math.Round((double)VramReservedMb / VramTotalMb * 100, 2);
}
