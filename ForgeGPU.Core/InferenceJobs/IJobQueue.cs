namespace ForgeGPU.Core.InferenceJobs;

public interface IJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, WeightBand weightBand, CancellationToken cancellationToken);
    ValueTask<JobIngressMessage> DequeueAsync(CancellationToken cancellationToken);
    ValueTask<long?> GetIngressDepthAsync(CancellationToken cancellationToken);
}
