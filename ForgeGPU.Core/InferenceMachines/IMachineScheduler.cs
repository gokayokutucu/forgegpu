using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Core.InferenceMachines;

public interface IMachineScheduler
{
    MachineSchedulingDecision Evaluate(
        InferenceJob job,
        JobResourceEstimate estimate,
        IReadOnlyCollection<MachineState> machines);
}
