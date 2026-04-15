using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Api.Contracts;

public sealed record JobDetailsResponse(
    Guid Id,
    string Prompt,
    string? Model,
    int Weight,
    WeightBand WeightBand,
    int RequiredMemoryMb,
    int RetryCount,
    int MaxRetries,
    string? LastFailureReason,
    JobFailureCategory? LastFailureCategory,
    DateTime? LastAttemptAtUtc,
    JobStatus Status,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Result,
    string? Error);
