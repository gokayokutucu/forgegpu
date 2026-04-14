using System.ComponentModel.DataAnnotations;

namespace ForgeGPU.Api.Contracts;

public sealed class SubmitJobRequest
{
    [Required]
    [MinLength(1)]
    public string Prompt { get; init; } = string.Empty;

    public string? Model { get; init; }
}
