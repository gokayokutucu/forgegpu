using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Api.Contracts;

public sealed record JobDetailsResponse(
    Guid Id,
    string Prompt,
    string? Model,
    JobStatus Status,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Result,
    string? Error);
