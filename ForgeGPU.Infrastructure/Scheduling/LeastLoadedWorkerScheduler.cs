using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceWorkers;

namespace ForgeGPU.Infrastructure.Scheduling;

public sealed class LeastLoadedWorkerScheduler : IWorkerScheduler
{
    public WorkerSchedulingDecision Evaluate(InferenceJob job, IReadOnlyCollection<WorkerState> workers)
    {
        var evaluations = new List<WorkerEligibilityResult>(workers.Count);

        foreach (var worker in workers.OrderBy(x => x.WorkerId, StringComparer.Ordinal))
        {
            var reason = EvaluateEligibility(job, worker);
            var eligible = reason is null;
            evaluations.Add(new WorkerEligibilityResult(worker.WorkerId, eligible, reason ?? "Eligible"));
        }

        var selectedWorker = workers
            .Where(x => evaluations.Any(e => e.WorkerId == x.WorkerId && e.IsEligible))
            .OrderByDescending(x => string.Equals(x.CurrentModel, job.Model, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.ActiveJobCount)
            .ThenByDescending(x => x.VramAvailableMb)
            .ThenBy(x => x.WorkerId, StringComparer.Ordinal)
            .FirstOrDefault();

        return new WorkerSchedulingDecision(selectedWorker, evaluations);
    }

    private static string? EvaluateEligibility(InferenceJob job, WorkerState worker)
    {
        if (job.Model is null)
        {
            return "Job model is not resolved";
        }

        if (!worker.SupportedModels.Contains(job.Model, StringComparer.OrdinalIgnoreCase))
        {
            return "ModelNotSupported";
        }

        if (worker.ActiveJobCount >= worker.MaxConcurrentJobs)
        {
            return "NoExecutionSlot";
        }

        if (worker.VramAvailableMb < job.RequiredMemoryMb)
        {
            return "InsufficientVram";
        }

        return null;
    }
}
