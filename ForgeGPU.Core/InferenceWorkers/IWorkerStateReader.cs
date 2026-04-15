namespace ForgeGPU.Core.InferenceWorkers;

public interface IWorkerStateReader
{
    IReadOnlyCollection<WorkerState> GetWorkers();
    int GetPendingJobCount();
    string GetSchedulerPolicy();
}
