namespace ForgeGPU.Core.InferenceJobs;

public static class ReliabilityPolicy
{
    public static bool IsRetryable(JobFailureCategory category)
    {
        return category is JobFailureCategory.Timeout or JobFailureCategory.ExecutionError;
    }

    public static JobFailureCategory ResolveTerminalCategory(JobFailureCategory category, bool canRetry)
    {
        return IsRetryable(category) && !canRetry
            ? JobFailureCategory.RetryExhausted
            : category;
    }
}
