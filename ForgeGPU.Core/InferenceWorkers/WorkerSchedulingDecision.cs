namespace ForgeGPU.Core.InferenceWorkers;

public sealed record WorkerSchedulingDecision(
    WorkerState? SelectedWorker,
    IReadOnlyCollection<WorkerEligibilityResult> Evaluations);

public sealed record WorkerEligibilityResult(
    string WorkerId,
    bool IsEligible,
    string Reason);
