namespace ForgeGPU.Core.Observability;

public interface IOrchestrationTelemetry
{
    void RecordJobAccepted(string model);
    void RecordJobDeferred(string reason);
    ValueTask<OrchestrationMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
