using FluentAssertions;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Scheduling;

namespace ForgeGPU.UnitTests.Scheduling;

public sealed class ResourceEstimatorTests
{
    private readonly ResourceEstimator _estimator = new();

    [Fact]
    public void Estimate_ShouldBeDeterministic_ForSameInput()
    {
        var job = new InferenceJob("prompt", "gpt-sim-b", weight: 300, requiredMemoryMb: 4096);

        var first = _estimator.Estimate(job);
        var second = _estimator.Estimate(job);

        first.Should().Be(second);
        job.Weight.Should().Be(300);
        first.WeightBand.Should().Be(job.WeightBand);
    }

    [Fact]
    public void Estimate_ShouldApplyWeightMemoryAndModelFactors()
    {
        var job = new InferenceJob("prompt", "gpt-sim-b", weight: 300, requiredMemoryMb: 4096);

        var estimate = _estimator.Estimate(job);

        estimate.WeightCostUnits.Should().Be(2);
        estimate.MemoryCostUnits.Should().Be(2);
        estimate.ModelCostUnits.Should().Be(2);
        estimate.EffectiveCostUnits.Should().Be(6);
        estimate.Model.Should().Be("gpt-sim-b");
    }

    [Fact]
    public void Estimate_ShouldIncreaseWhenMemoryOrModelCostIncrease()
    {
        var baseline = new InferenceJob("prompt", "gpt-sim-a", weight: 150, requiredMemoryMb: 2048);
        var heavierMemory = new InferenceJob("prompt", "gpt-sim-a", weight: 150, requiredMemoryMb: 6144);
        var heavierModel = new InferenceJob("prompt", "gpt-sim-mix", weight: 150, requiredMemoryMb: 2048);

        var baselineEstimate = _estimator.Estimate(baseline);
        var memoryEstimate = _estimator.Estimate(heavierMemory);
        var modelEstimate = _estimator.Estimate(heavierModel);

        memoryEstimate.EffectiveCostUnits.Should().BeGreaterThan(baselineEstimate.EffectiveCostUnits);
        modelEstimate.EffectiveCostUnits.Should().BeGreaterThan(baselineEstimate.EffectiveCostUnits);
    }
}
