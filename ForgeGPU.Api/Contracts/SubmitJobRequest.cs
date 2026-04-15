using System.ComponentModel.DataAnnotations;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Api.Contracts;

public sealed class SubmitJobRequest
{
    [Required]
    [MinLength(1)]
    public string Prompt { get; init; } = string.Empty;

    public string? Model { get; init; }

    [Range(InferenceJob.MinWeight, InferenceJob.MaxWeight)]
    public int? Weight { get; init; }

    [Range(InferenceJob.MinRequiredMemoryMb, InferenceJob.MaxRequiredMemoryMb)]
    public int? RequiredMemoryMb { get; init; }
}
