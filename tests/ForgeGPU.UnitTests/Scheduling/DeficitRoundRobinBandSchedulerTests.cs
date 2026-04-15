using FluentAssertions;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Scheduling;

namespace ForgeGPU.UnitTests.Scheduling;

public sealed class DeficitRoundRobinBandSchedulerTests
{
    [Fact]
    public void TrySelectNext_ShouldUseExactWeightAsDebit()
    {
        var scheduler = new DeficitRoundRobinBandScheduler();
        scheduler.Enqueue(Guid.NewGuid(), WeightBand.W41Plus, 41);

        var selected = scheduler.TrySelectNext(out var job, out var creditBefore, out var creditAfter);

        selected.Should().BeTrue();
        job.WeightBand.Should().Be(WeightBand.W41Plus);
        creditBefore.Should().Be(60);
        creditAfter.Should().Be(19);
    }

    [Fact]
    public void TrySelectNext_ShouldAccumulateCreditForHeavyJobs()
    {
        var scheduler = new DeficitRoundRobinBandScheduler();
        scheduler.Enqueue(Guid.NewGuid(), WeightBand.W41Plus, 100);

        var selected = scheduler.TrySelectNext(out var job, out var creditBefore, out var creditAfter);

        selected.Should().BeTrue();
        job.ExactWeight.Should().Be(100);
        creditBefore.Should().Be(120);
        creditAfter.Should().Be(20);
    }

    [Fact]
    public void TrySelectNext_ShouldNotStarveHeavyJobsBehindLightJobs()
    {
        var scheduler = new DeficitRoundRobinBandScheduler();
        for (var i = 0; i < 5; i++)
        {
            scheduler.Enqueue(Guid.NewGuid(), WeightBand.W1_2, 2);
        }

        var heavyJobId = Guid.NewGuid();
        scheduler.Enqueue(heavyJobId, WeightBand.W41Plus, 100);

        var selectedIds = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            scheduler.TrySelectNext(out var selected, out _, out _).Should().BeTrue();
            selectedIds.Add(selected.JobId);
        }

        selectedIds.Should().Contain(heavyJobId);
    }

    [Fact]
    public void Snapshot_ShouldExposeCurrentCreditsAndBufferDepths()
    {
        var scheduler = new DeficitRoundRobinBandScheduler();
        scheduler.Enqueue(Guid.NewGuid(), WeightBand.W3_5, 5);
        scheduler.TrySelectNext(out _, out _, out _).Should().BeTrue();

        var snapshot = scheduler.GetSnapshot();

        snapshot.BandBufferDepths["W3_5"].Should().Be(0);
        snapshot.BandCredits["W3_5"].Should().Be(0);
    }
}
