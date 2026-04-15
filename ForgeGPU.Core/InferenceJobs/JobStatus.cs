namespace ForgeGPU.Core.InferenceJobs;

public enum JobStatus
{
    Queued = 0,
    Processing = 1,
    Retrying = 2,
    Completed = 3,
    Failed = 4,
    DeadLettered = 5
}
