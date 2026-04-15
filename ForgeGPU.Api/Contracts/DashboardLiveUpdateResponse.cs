using ForgeGPU.Core.Observability;

namespace ForgeGPU.Api.Contracts;

public sealed record DashboardLiveUpdateResponse(
    DateTime GeneratedAtUtc,
    MetricsResponse Metrics,
    MachinesSnapshotResponse Machines,
    IReadOnlyCollection<OperationalEvent> Events);
