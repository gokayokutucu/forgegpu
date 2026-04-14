namespace ForgeGPU.Core.InferenceJobs;

public interface IJobStore
{
    Task AddAsync(InferenceJob job, CancellationToken cancellationToken);
    Task<InferenceJob?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(InferenceJob job, CancellationToken cancellationToken);
}
