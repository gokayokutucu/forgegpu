using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Core.InferenceWorkers;

public interface IWorkerScheduler
{
    WorkerSchedulingDecision Evaluate(InferenceJob job, IReadOnlyCollection<WorkerState> workers);
}
