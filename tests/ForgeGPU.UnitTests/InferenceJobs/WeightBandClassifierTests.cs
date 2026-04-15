using FluentAssertions;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.UnitTests.InferenceJobs;

public sealed class WeightBandClassifierTests
{
    [Theory]
    [InlineData(1, WeightBand.W1_2)]
    [InlineData(2, WeightBand.W1_2)]
    [InlineData(3, WeightBand.W3_5)]
    [InlineData(5, WeightBand.W3_5)]
    [InlineData(6, WeightBand.W6_10)]
    [InlineData(10, WeightBand.W6_10)]
    [InlineData(11, WeightBand.W11_20)]
    [InlineData(20, WeightBand.W11_20)]
    [InlineData(21, WeightBand.W21_40)]
    [InlineData(40, WeightBand.W21_40)]
    [InlineData(41, WeightBand.W41Plus)]
    [InlineData(1000, WeightBand.W41Plus)]
    public void Classify_ShouldMapExpectedBoundaries(int weight, WeightBand expectedBand)
    {
        var actual = WeightBandClassifier.Classify(weight);

        actual.Should().Be(expectedBand);
    }
}
