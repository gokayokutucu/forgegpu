using ForgeGPU.Core.InferenceMachines;

namespace ForgeGPU.Api.Contracts;

public sealed record MachineStateResponse(
    string MachineId,
    MachineMetadataResponse Durable,
    MachineLiveStateResponse Live,
    MachineAvailabilityResponse Availability);

public sealed record MachineMetadataResponse(
    string Name,
    bool Enabled,
    int TotalCapacityUnits,
    int CpuScore,
    int RamMb,
    int GpuVramMb,
    int MaxParallelWorkers,
    IReadOnlyCollection<string> SupportedModels,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record MachineLiveStateResponse(
    string? ActorInstanceId,
    MachineActorStatus? ActorStatus,
    MachineStatus RuntimeStatus,
    DateTime? LastHeartbeatUtc,
    int UsedCapacityUnits,
    int RemainingCapacityUnits,
    double CapacityUtilizationPercent,
    int ActiveJobCount,
    double ParallelUtilizationPercent,
    int ReservedVramMb,
    int RemainingGpuVramMb,
    double GpuVramUtilizationPercent,
    IReadOnlyCollection<Guid> RunningJobIds,
    string? CurrentModel,
    int CurrentBatchSize,
    int TotalBatchesFormed,
    string? LastBatchModel,
    int LastBatchSize,
    DateTime? LastBatchCompletedUtc,
    int CompletedJobCount,
    int FailedJobCount);

public sealed record MachineAvailabilityResponse(
    MachineLivenessState LivenessState,
    bool IsAvailableForScheduling);

public sealed record MachinesSnapshotResponse(
    string SchedulerPolicy,
    int PendingJobCount,
    IReadOnlyCollection<MachineStateResponse> Machines);
