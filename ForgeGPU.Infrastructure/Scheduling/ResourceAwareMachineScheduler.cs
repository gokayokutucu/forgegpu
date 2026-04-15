using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceMachines;

namespace ForgeGPU.Infrastructure.Scheduling;

public sealed class ResourceAwareMachineScheduler : IMachineScheduler
{
    public MachineSchedulingDecision Evaluate(
        InferenceJob job,
        JobResourceEstimate estimate,
        IReadOnlyCollection<MachineState> machines)
    {
        var evaluations = new List<MachineEligibilityResult>(machines.Count);

        foreach (var machine in machines.OrderBy(x => x.MachineId, StringComparer.Ordinal))
        {
            var reason = EvaluateEligibility(job, estimate, machine);
            evaluations.Add(new MachineEligibilityResult(
                machine.MachineId,
                reason is null,
                reason ?? "Eligible",
                machine.RemainingCapacityUnits,
                machine.RemainingGpuVramMb,
                Math.Max(0, machine.MaxParallelWorkers - machine.ActiveJobCount)));
        }

        var selectedMachine = machines
            .Where(machine => evaluations.Any(x => x.MachineId == machine.MachineId && x.IsEligible))
            .OrderBy(machine => machine.RemainingCapacityUnits - estimate.EffectiveCostUnits)
            .ThenBy(machine => machine.ActiveJobCount)
            .ThenByDescending(machine => machine.RemainingGpuVramMb)
            .ThenBy(machine => machine.MachineId, StringComparer.Ordinal)
            .FirstOrDefault();

        return new MachineSchedulingDecision(selectedMachine, evaluations);
    }

    private static string? EvaluateEligibility(InferenceJob job, JobResourceEstimate estimate, MachineState machine)
    {
        if (!machine.Enabled)
        {
            return "MachineDisabled";
        }

        if (machine.LivenessState == MachineLivenessState.Offline)
        {
            return "MachineOffline";
        }

        if (machine.LivenessState == MachineLivenessState.Stale)
        {
            return "MachineStale";
        }

        if (machine.LivenessState == MachineLivenessState.Unavailable)
        {
            return "MachineUnavailable";
        }

        if (!machine.IsAvailableForScheduling)
        {
            return "MachineUnavailableForScheduling";
        }

        if (job.Model is null)
        {
            return "JobModelNotResolved";
        }

        if (!machine.SupportedModels.Contains(job.Model, StringComparer.OrdinalIgnoreCase))
        {
            return "ModelNotSupported";
        }

        if (machine.ActiveJobCount >= machine.MaxParallelWorkers)
        {
            return "NoExecutionSlot";
        }

        if (machine.RemainingCapacityUnits < estimate.EffectiveCostUnits)
        {
            return "InsufficientCapacityUnits";
        }

        if (machine.RemainingGpuVramMb < job.RequiredMemoryMb)
        {
            return "InsufficientGpuVram";
        }

        return null;
    }
}
