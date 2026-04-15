namespace ForgeGPU.Core.InferenceMachines;

public interface IMachineLiveProjectionStore
{
    ValueTask PublishAsync(MachineLiveProjection projection, TimeSpan ttl, CancellationToken cancellationToken);
    ValueTask PublishOfflineAsync(MachineLiveProjection projection, TimeSpan ttl, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, MachineLiveProjection>> GetManyAsync(
        IReadOnlyCollection<string> machineIds,
        CancellationToken cancellationToken);
}
