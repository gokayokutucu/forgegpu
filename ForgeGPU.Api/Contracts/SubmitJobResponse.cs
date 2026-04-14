using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Api.Contracts;

public sealed record SubmitJobResponse(Guid Id, JobStatus Status, string StatusEndpoint);
