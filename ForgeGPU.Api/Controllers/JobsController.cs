using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.Observability;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly IJobQueue _jobQueue;
    private readonly IJobStore _jobStore;
    private readonly IOrchestrationTelemetry _telemetry;
    private readonly ILogger<JobsController> _logger;
    private readonly InfrastructureOptions _options;

    public JobsController(
        IJobQueue jobQueue,
        IJobStore jobStore,
        IOrchestrationTelemetry telemetry,
        IOptions<InfrastructureOptions> options,
        ILogger<JobsController> logger)
    {
        _jobQueue = jobQueue;
        _jobStore = jobStore;
        _telemetry = telemetry;
        _logger = logger;
        _options = options.Value;
    }

    [HttpPost]
    [ProducesResponseType<SubmitJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJob([FromBody] SubmitJobRequest request, CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _options.Scheduling.DefaultModel
            : request.Model;

        var requiredMemoryMb = request.RequiredMemoryMb
            ?? ResolveRequiredMemoryForModel(model);

        var weight = request.Weight ?? InferenceJob.DefaultWeight;
        var job = new InferenceJob(
            request.Prompt,
            model,
            weight,
            requiredMemoryMb,
            _options.Reliability.MaxRetries);

        await _jobStore.AddAsync(job, cancellationToken);
        await _jobQueue.EnqueueAsync(job.Id, cancellationToken);
        _telemetry.RecordJobAccepted(job.Model ?? "unknown");

        _logger.LogInformation(
            "Accepted inference job {JobId}. Model: {Model}. Weight: {Weight}. RequiredMemoryMb: {RequiredMemoryMb}. PromptLength: {PromptLength}.",
            job.Id,
            job.Model,
            job.Weight,
            job.RequiredMemoryMb,
            job.Prompt.Length);

        var response = new SubmitJobResponse(job.Id, job.Status, $"/jobs/{job.Id}");
        return Accepted(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<JobDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var response = new JobDetailsResponse(
            job.Id,
            job.Prompt,
            job.Model,
            job.Weight,
            job.RequiredMemoryMb,
            job.RetryCount,
            job.MaxRetries,
            job.LastFailureReason,
            job.LastFailureCategory,
            job.LastAttemptAtUtc,
            job.Status,
            job.CreatedAtUtc,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.Result,
            job.Error);

        return Ok(response);
    }

    [HttpGet("dead-letter")]
    [ProducesResponseType<IReadOnlyCollection<JobDetailsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeadLetterJobs([FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var jobs = await _jobStore.ListDeadLetteredAsync(limit, cancellationToken);

        var response = jobs.Select(job => new JobDetailsResponse(
                job.Id,
                job.Prompt,
                job.Model,
                job.Weight,
                job.RequiredMemoryMb,
                job.RetryCount,
                job.MaxRetries,
                job.LastFailureReason,
                job.LastFailureCategory,
                job.LastAttemptAtUtc,
                job.Status,
                job.CreatedAtUtc,
                job.StartedAtUtc,
                job.CompletedAtUtc,
                job.Result,
                job.Error))
            .ToArray();

        return Ok(response);
    }

    private int ResolveRequiredMemoryForModel(string model)
    {
        var configuredModel = _options.Scheduling.ModelDefaults
            .FirstOrDefault(x => string.Equals(x.Model, model, StringComparison.OrdinalIgnoreCase));

        return configuredModel?.RequiredMemoryMb ?? _options.Scheduling.DefaultRequiredMemoryMb;
    }
}
