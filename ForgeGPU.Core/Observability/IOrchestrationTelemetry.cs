namespace ForgeGPU.Core.Observability;

using ForgeGPU.Core.InferenceJobs;

public interface IOrchestrationTelemetry
{
    void RecordJobAccepted(string model, WeightBand weightBand);
    void RecordIngressPublished(string topic, WeightBand weightBand);
    void RecordJobDeferred(string reason, WeightBand weightBand);
    ValueTask<OrchestrationMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    IReadOnlyCollection<OperationalEvent> GetRecentEvents(int limit = 100);
}
