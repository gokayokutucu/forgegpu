namespace ForgeGPU.Core.Observability;

using ForgeGPU.Core.InferenceJobs;

public interface IOrchestrationTelemetry
{
    void RecordJobAccepted(string model, WeightBand weightBand);
    void RecordJobDeferred(string reason, WeightBand weightBand);
    ValueTask<OrchestrationMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
