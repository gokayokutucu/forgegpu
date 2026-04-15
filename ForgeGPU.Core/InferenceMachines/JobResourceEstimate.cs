using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Core.InferenceMachines;

public sealed record JobResourceEstimate(
    string Model,
    int EffectiveCostUnits,
    int WeightCostUnits,
    int MemoryCostUnits,
    int ModelCostUnits,
    WeightBand WeightBand);
