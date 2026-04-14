using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceJobs;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly IJobQueue _jobQueue;
    private readonly IJobStore _jobStore;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IJobQueue jobQueue, IJobStore jobStore, ILogger<JobsController> logger)
    {
        _jobQueue = jobQueue;
        _jobStore = jobStore;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType<SubmitJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJob([FromBody] SubmitJobRequest request, CancellationToken cancellationToken)
    {
        var job = new InferenceJob(request.Prompt, request.Model);

        await _jobStore.AddAsync(job, cancellationToken);
        await _jobQueue.EnqueueAsync(job.Id, cancellationToken);

        _logger.LogInformation(
            "Accepted inference job {JobId}. Model: {Model}. PromptLength: {PromptLength}.",
            job.Id,
            job.Model,
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
            job.Status,
            job.CreatedAtUtc,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.Result,
            job.Error);

        return Ok(response);
    }
}
