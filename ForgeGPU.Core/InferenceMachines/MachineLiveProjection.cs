namespace ForgeGPU.Core.InferenceMachines;

public sealed record MachineLiveProjection(
    string MachineId,
    string ActorInstanceId,
    MachineActorStatus ActorStatus,
    DateTime LastHeartbeatUtc,
    int UsedCapacityUnits,
    int RemainingCapacityUnits,
    int ActiveJobCount,
    int ReservedVramMb,
    IReadOnlyCollection<Guid> RunningJobIds,
    int CurrentBatchSize,
    string? CurrentModel,
    MachineStatus RuntimeStatus);
