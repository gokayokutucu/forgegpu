using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeGPU.Infrastructure.Workers;

public sealed class InferenceWorker : BackgroundService
{
    private readonly IJobQueue _jobQueue;
    private readonly IJobStore _jobStore;
    private readonly ILogger<InferenceWorker> _logger;
    private readonly InfrastructureOptions _infrastructureOptions;

    public InferenceWorker(
        IJobQueue jobQueue,
        IJobStore jobStore,
        IOptions<InfrastructureOptions> infrastructureOptions,
        ILogger<InferenceWorker> logger)
    {
        _jobQueue = jobQueue;
        _jobStore = jobStore;
        _logger = logger;
        _infrastructureOptions = infrastructureOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inference worker started. QueueProvider: {QueueProvider}. JobStoreProvider: {JobStoreProvider}. Redis: {RedisConnection}. Postgres: {PostgresConnection}.",
            _infrastructureOptions.Runtime.QueueProvider,
            _infrastructureOptions.Runtime.JobStoreProvider,
            _infrastructureOptions.Redis.ConnectionString,
            _infrastructureOptions.Postgres.ConnectionString);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;

            try
            {
                jobId = await _jobQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var job = await _jobStore.GetAsync(jobId, stoppingToken);
            if (job is null)
            {
                _logger.LogWarning("Dequeued job {JobId} but it was not found in the store.", jobId);
                continue;
            }

            try
            {
                var startedAtUtc = DateTime.UtcNow;
                job.MarkProcessing(startedAtUtc);
                await _jobStore.UpdateAsync(job, stoppingToken);

                _logger.LogInformation(
                    "Job {JobId} is processing. Model: {Model}. PromptLength: {PromptLength}.",
                    job.Id,
                    job.Model,
                    job.Prompt.Length);

                var delayMs = Random.Shared.Next(1500, 2501);
                await Task.Delay(delayMs, stoppingToken);

                var simulatedResult = $"Simulated response for prompt: {job.Prompt}";
                var completedAtUtc = DateTime.UtcNow;

                job.MarkCompleted(simulatedResult, completedAtUtc);
                await _jobStore.UpdateAsync(job, stoppingToken);

                _logger.LogInformation("Job {JobId} completed in {DurationMs}ms.", job.Id, delayMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var failedAtUtc = DateTime.UtcNow;
                job.MarkFailed(ex.Message, failedAtUtc);
                await _jobStore.UpdateAsync(job, CancellationToken.None);

                _logger.LogError(ex, "Job {JobId} failed.", job.Id);
            }
        }

        _logger.LogInformation("Inference worker stopped.");
    }
}
