namespace ForgeGPU.Core.InferenceJobs;

public sealed class InferenceJob
{
    public Guid Id { get; }
    public string Prompt { get; }
    public string? Model { get; }
    public JobStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? Result { get; private set; }
    public string? Error { get; private set; }

    public InferenceJob(string prompt, string? model)
    {
        Id = Guid.NewGuid();
        Prompt = prompt;
        Model = model;
        Status = JobStatus.Queued;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void MarkProcessing(DateTime startedAtUtc)
    {
        Status = JobStatus.Processing;
        StartedAtUtc = startedAtUtc;
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

    public void MarkFailed(string error, DateTime completedAtUtc)
    {
        Status = JobStatus.Failed;
        Error = error;
        Result = null;
        CompletedAtUtc = completedAtUtc;
    }
}
