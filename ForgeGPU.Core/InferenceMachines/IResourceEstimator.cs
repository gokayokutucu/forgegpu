using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Core.InferenceMachines;

public interface IResourceEstimator
{
    JobResourceEstimate Estimate(InferenceJob job);
}
