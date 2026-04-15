namespace ForgeGPU.Core.Observability;

using ForgeGPU.Core.InferenceJobs;

public sealed record OperationalEvent(
    DateTime TimestampUtc,
    string Kind,
    string Summary,
    Guid? JobId = null,
    string? PromptPreview = null,
    WeightBand? WeightBand = null,
    string? TransportLane = null,
    string? MachineId = null,
    string? Model = null,
    int? ExactWeight = null,
    int? CreditBefore = null,
    int? CreditAfter = null,
    int? BatchSize = null,
    string? Reason = null);
