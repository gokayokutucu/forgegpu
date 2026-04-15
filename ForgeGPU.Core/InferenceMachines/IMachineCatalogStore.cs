namespace ForgeGPU.Core.InferenceMachines;

public interface IMachineCatalogStore
{
    Task<IReadOnlyCollection<MachineCatalogEntry>> ListAsync(CancellationToken cancellationToken);
}
