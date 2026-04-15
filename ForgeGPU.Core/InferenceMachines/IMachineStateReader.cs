namespace ForgeGPU.Core.InferenceMachines;

public interface IMachineStateReader
{
    Task<IReadOnlyCollection<MachineState>> GetMachinesAsync(CancellationToken cancellationToken);
    int GetPendingJobCount();
    string GetSchedulerPolicy();
}
