namespace ForgeGPU.Core.InferenceMachines;

public sealed record MachineState(
    string MachineId,
    string Name,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int TotalCapacityUnits,
    int UsedCapacityUnits,
    int CpuScore,
    int RamMb,
    int GpuVramMb,
    int UsedGpuVramMb,
    int ActiveJobCount,
    int MaxParallelWorkers,
    DateTime? LastHeartbeatUtc,
    MachineStatus Status,
    MachineActorStatus? ActorStatus,
    MachineLivenessState LivenessState,
    bool IsAvailableForScheduling,
    string? ActorInstanceId,
    IReadOnlyCollection<string> SupportedModels,
    IReadOnlyCollection<Guid> RunningJobIds,
    string? CurrentModel,
    int CurrentBatchSize,
    int TotalBatchesFormed,
    string? LastBatchModel,
    int LastBatchSize,
    DateTime? LastBatchCompletedUtc,
    int CompletedJobCount,
    int FailedJobCount)
{
    public int RemainingCapacityUnits => Math.Max(0, TotalCapacityUnits - UsedCapacityUnits);
    public int RemainingGpuVramMb => Math.Max(0, GpuVramMb - UsedGpuVramMb);
    public double CapacityUtilizationPercent => TotalCapacityUnits == 0
        ? 0
        : Math.Round((double)UsedCapacityUnits / TotalCapacityUnits * 100, 2);
    public double GpuVramUtilizationPercent => GpuVramMb == 0
        ? 0
        : Math.Round((double)UsedGpuVramMb / GpuVramMb * 100, 2);
    public double ParallelUtilizationPercent => MaxParallelWorkers == 0
        ? 0
        : Math.Round((double)ActiveJobCount / MaxParallelWorkers * 100, 2);
}
