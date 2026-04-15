namespace ForgeGPU.Core.InferenceJobs;

public sealed class InferenceJob
{
    public const int DefaultWeight = 100;
    public const int MinWeight = 1;
    public const int MaxWeight = 1000;
    public const int DefaultMaxRetries = 2;
    public const int MinMaxRetries = 0;
    public const int MaxMaxRetries = 10;

    public const int DefaultRequiredMemoryMb = 4096;
    public const int MinRequiredMemoryMb = 256;
    public const int MaxRequiredMemoryMb = 24576;

    public Guid Id { get; }
    public string Prompt { get; }
    public string? Model { get; }
    public int Weight { get; }
    public WeightBand WeightBand => WeightBandClassifier.Classify(Weight);
    public int RequiredMemoryMb { get; }
    public int RetryCount { get; private set; }
    public int MaxRetries { get; }
    public string? LastFailureReason { get; private set; }
    public JobFailureCategory? LastFailureCategory { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public JobStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? Result { get; private set; }
    public string? Error { get; private set; }

    public InferenceJob(
        string prompt,
        string? model,
        int weight = DefaultWeight,
        int requiredMemoryMb = DefaultRequiredMemoryMb,
        int maxRetries = DefaultMaxRetries)
    {
        ValidateWeight(weight);
        ValidateRequiredMemoryMb(requiredMemoryMb);
        ValidateMaxRetries(maxRetries);

        Id = Guid.NewGuid();
        Prompt = prompt;
        Model = model;
        Weight = weight;
        RequiredMemoryMb = requiredMemoryMb;
        MaxRetries = maxRetries;
        Status = JobStatus.Queued;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private InferenceJob(
        Guid id,
        string prompt,
        string? model,
        int weight,
        int requiredMemoryMb,
        int retryCount,
        int maxRetries,
        string? lastFailureReason,
        JobFailureCategory? lastFailureCategory,
        DateTime? lastAttemptAtUtc,
        JobStatus status,
        DateTime createdAtUtc,
        DateTime? startedAtUtc,
        DateTime? completedAtUtc,
        string? result,
        string? error)
    {
        ValidateWeight(weight);
        ValidateRequiredMemoryMb(requiredMemoryMb);
        ValidateMaxRetries(maxRetries);

        Id = id;
        Prompt = prompt;
        Model = model;
        Weight = weight;
        RequiredMemoryMb = requiredMemoryMb;
        RetryCount = retryCount;
        MaxRetries = maxRetries;
        LastFailureReason = lastFailureReason;
        LastFailureCategory = lastFailureCategory;
        LastAttemptAtUtc = lastAttemptAtUtc;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Result = result;
        Error = error;
    }

    public static InferenceJob Rehydrate(
        Guid id,
        string prompt,
        string? model,
        int weight,
        int requiredMemoryMb,
        int retryCount,
        int maxRetries,
        string? lastFailureReason,
        JobFailureCategory? lastFailureCategory,
        DateTime? lastAttemptAtUtc,
        JobStatus status,
        DateTime createdAtUtc,
        DateTime? startedAtUtc,
        DateTime? completedAtUtc,
        string? result,
        string? error)
    {
        return new InferenceJob(
            id,
            prompt,
            model,
            weight,
            requiredMemoryMb,
            retryCount,
            maxRetries,
            lastFailureReason,
            lastFailureCategory,
            lastAttemptAtUtc,
            status,
            createdAtUtc,
            startedAtUtc,
            completedAtUtc,
            result,
            error);
    }

    public void MarkProcessing(DateTime startedAtUtc)
    {
        Status = JobStatus.Processing;
        StartedAtUtc = startedAtUtc;
        LastAttemptAtUtc = startedAtUtc;
        CompletedAtUtc = null;
        Result = null;
        Error = null;
    }

    public void MarkCompleted(string result, DateTime completedAtUtc)
    {
        Status = JobStatus.Completed;
        Result = result;
        Error = null;
        CompletedAtUtc = completedAtUtc;
    }

    public void MarkRetrying(string reason, JobFailureCategory category, DateTime retryScheduledAtUtc)
    {
        RetryCount++;
        Status = JobStatus.Retrying;
        StartedAtUtc = null;
        CompletedAtUtc = null;
        Result = null;
        Error = reason;
        LastFailureReason = reason;
        LastFailureCategory = category;
        LastAttemptAtUtc = retryScheduledAtUtc;
    }

    public void MarkFailed(string error, JobFailureCategory category, DateTime completedAtUtc)
    {
        Status = JobStatus.Failed;
        Error = error;
        Result = null;
        CompletedAtUtc = completedAtUtc;
        LastFailureReason = error;
        LastFailureCategory = category;
        LastAttemptAtUtc = completedAtUtc;
    }

    public void MarkDeadLettered(string error, JobFailureCategory category, DateTime completedAtUtc)
    {
        Status = JobStatus.DeadLettered;
        Error = error;
        Result = null;
        CompletedAtUtc = completedAtUtc;
        LastFailureReason = error;
        LastFailureCategory = category;
        LastAttemptAtUtc = completedAtUtc;
    }

    public bool CanRetry()
    {
        return RetryCount < MaxRetries;
    }

    private static void ValidateWeight(int weight)
    {
        if (weight is < MinWeight or > MaxWeight)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), $"Weight must be between {MinWeight} and {MaxWeight}.");
        }
    }

    private static void ValidateRequiredMemoryMb(int requiredMemoryMb)
    {
        if (requiredMemoryMb is < MinRequiredMemoryMb or > MaxRequiredMemoryMb)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredMemoryMb),
                $"RequiredMemoryMb must be between {MinRequiredMemoryMb} and {MaxRequiredMemoryMb}.");
        }
    }

    private static void ValidateMaxRetries(int maxRetries)
    {
        if (maxRetries is < MinMaxRetries or > MaxMaxRetries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetries),
                $"MaxRetries must be between {MinMaxRetries} and {MaxMaxRetries}.");
        }
    }
}
