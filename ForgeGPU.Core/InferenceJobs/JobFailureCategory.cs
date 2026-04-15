namespace ForgeGPU.Core.InferenceJobs;

public enum JobFailureCategory
{
    Timeout = 0,
    ExecutionError = 1,
    CapacityUnavailable = 2,
    ValidationError = 3,
    NonRetryableError = 4,
    RetryExhausted = 5
}
