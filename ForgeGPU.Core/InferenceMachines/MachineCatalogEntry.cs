namespace ForgeGPU.Core.InferenceMachines;

public sealed record MachineCatalogEntry(
    string MachineId,
    string Name,
    bool Enabled,
    int TotalCapacityUnits,
    int CpuScore,
    int RamMb,
    int GpuVramMb,
    int MaxParallelWorkers,
    IReadOnlyCollection<string> SupportedModels,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
