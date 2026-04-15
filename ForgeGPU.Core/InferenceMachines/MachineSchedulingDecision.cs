namespace ForgeGPU.Core.InferenceMachines;

public sealed record MachineSchedulingDecision(
    MachineState? SelectedMachine,
    IReadOnlyCollection<MachineEligibilityResult> Evaluations);

public sealed record MachineEligibilityResult(
    string MachineId,
    bool IsEligible,
    string Reason,
    int RemainingCapacityUnits,
    int RemainingGpuVramMb,
    int AvailableWorkerSlots);
