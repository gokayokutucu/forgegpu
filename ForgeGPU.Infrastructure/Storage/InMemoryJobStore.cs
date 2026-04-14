using System.Collections.Concurrent;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Infrastructure.Storage;

public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, InferenceJob> _jobs = new();

    public Task AddAsync(InferenceJob job, CancellationToken cancellationToken)
    {
        _jobs.TryAdd(job.Id, job);
        return Task.CompletedTask;
    }

    public Task<InferenceJob?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        _jobs.TryGetValue(id, out var job);
        return Task.FromResult(job);
    }

    public Task UpdateAsync(InferenceJob job, CancellationToken cancellationToken)
    {
        _jobs.AddOrUpdate(job.Id, job, (_, _) => job);
        return Task.CompletedTask;
    }
}
