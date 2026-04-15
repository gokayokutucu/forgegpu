namespace ForgeGPU.Core.InferenceJobs;

public sealed record JobIngressMessage(
    Guid JobId,
    WeightBand WeightBand,
    string TransportLane);
