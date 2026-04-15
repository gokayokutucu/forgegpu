using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceMachines;

namespace ForgeGPU.Infrastructure.Scheduling;

public sealed class ResourceEstimator : IResourceEstimator
{
    public JobResourceEstimate Estimate(InferenceJob job)
    {
        var resolvedModel = string.IsNullOrWhiteSpace(job.Model) ? "unknown" : job.Model;
        var weightUnits = Math.Max(1, (int)Math.Ceiling(job.Weight / 150d));
        var memoryUnits = Math.Max(1, (int)Math.Ceiling(job.RequiredMemoryMb / 2048d));
        var modelUnits = resolvedModel.ToLowerInvariant() switch
        {
            "gpt-sim-b" => 2,
            "gpt-sim-mix" => 2,
            _ => 1
        };

        var effectiveCostUnits = weightUnits + memoryUnits + modelUnits;
        return new JobResourceEstimate(
            resolvedModel,
            effectiveCostUnits,
            weightUnits,
            memoryUnits,
            modelUnits,
            job.WeightBand);
    }
}
