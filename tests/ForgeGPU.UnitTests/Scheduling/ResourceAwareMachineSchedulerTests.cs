using FluentAssertions;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceMachines;
using ForgeGPU.Infrastructure.Scheduling;

namespace ForgeGPU.UnitTests.Scheduling;

public sealed class ResourceAwareMachineSchedulerTests
{
    private readonly ResourceAwareMachineScheduler _scheduler = new();
    private readonly ResourceEstimator _estimator = new();

    [Fact]
    public void Evaluate_ShouldExcludeDisabledAndUnavailableMachines()
    {
        var job = new InferenceJob("prompt", "gpt-sim-a", weight: 10, requiredMemoryMb: 2048);
        var estimate = _estimator.Estimate(job);
        var machines = new[]
        {
            CreateMachine("machine-01", enabled: false),
            CreateMachine("machine-02", liveness: MachineLivenessState.Stale),
            CreateMachine("machine-03", liveness: MachineLivenessState.Live, isAvailable: true)
        };

        var decision = _scheduler.Evaluate(job, estimate, machines);

        decision.SelectedMachine.Should().NotBeNull();
        decision.SelectedMachine!.MachineId.Should().Be("machine-03");
        decision.Evaluations.Should().Contain(x => x.MachineId == "machine-01" && x.Reason == "MachineDisabled");
        decision.Evaluations.Should().Contain(x => x.MachineId == "machine-02" && x.Reason == "MachineStale");
    }

    [Fact]
    public void Evaluate_ShouldChooseSmallestNonNegativeRemainingCapacityFit()
    {
        var job = new InferenceJob("prompt", "gpt-sim-a", weight: 20, requiredMemoryMb: 2048);
        var estimate = _estimator.Estimate(job); // cost = 4
        var machines = new[]
        {
            CreateMachine("machine-01", totalCapacityUnits: 20, usedCapacityUnits: 2),
            CreateMachine("machine-02", totalCapacityUnits: 8, usedCapacityUnits: 0),
            CreateMachine("machine-03", totalCapacityUnits: 10, usedCapacityUnits: 2)
        };

        var decision = _scheduler.Evaluate(job, estimate, machines);

        decision.SelectedMachine.Should().NotBeNull();
        decision.SelectedMachine!.MachineId.Should().Be("machine-02");
    }

    [Fact]
    public void Evaluate_ShouldUseDeterministicTieBreak_WhenSlackMatches()
    {
        var job = new InferenceJob("prompt", "gpt-sim-a", weight: 20, requiredMemoryMb: 2048);
        var estimate = _estimator.Estimate(job); // cost = 4
        var machines = new[]
        {
            CreateMachine("machine-02", totalCapacityUnits: 8, usedCapacityUnits: 0, remainingGpuVramMbOverride: 4096),
            CreateMachine("machine-01", totalCapacityUnits: 8, usedCapacityUnits: 0, remainingGpuVramMbOverride: 4096)
        };

        var decision = _scheduler.Evaluate(job, estimate, machines);

        decision.SelectedMachine.Should().NotBeNull();
        decision.SelectedMachine!.MachineId.Should().Be("machine-01");
    }

    private static MachineState CreateMachine(
        string machineId,
        bool enabled = true,
        MachineLivenessState liveness = MachineLivenessState.Live,
        bool isAvailable = true,
        int totalCapacityUnits = 12,
        int usedCapacityUnits = 0,
        int gpuVramMb = 8192,
        int usedGpuVramMb = 0,
        int activeJobCount = 0,
        int maxParallelWorkers = 2,
        string[]? supportedModels = null,
        int? remainingGpuVramMbOverride = null)
    {
        var effectiveUsedGpu = remainingGpuVramMbOverride.HasValue
            ? gpuVramMb - remainingGpuVramMbOverride.Value
            : usedGpuVramMb;

        return new MachineState(
            machineId,
            machineId,
            enabled,
            DateTime.UtcNow,
            DateTime.UtcNow,
            totalCapacityUnits,
            usedCapacityUnits,
            10,
            16384,
            gpuVramMb,
            effectiveUsedGpu,
            activeJobCount,
            maxParallelWorkers,
            DateTime.UtcNow,
            activeJobCount == 0 ? MachineStatus.Idle : MachineStatus.Busy,
            MachineActorStatus.Online,
            liveness,
            isAvailable,
            $"actor-{machineId}",
            supportedModels ?? ["gpt-sim-a", "gpt-sim-b", "gpt-sim-mix"],
            Array.Empty<Guid>(),
            null,
            0,
            0,
            null,
            0,
            null,
            0,
            0);
    }
}
