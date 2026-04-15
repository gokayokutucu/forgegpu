using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceWorkers;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("workers")]
public sealed class WorkersController : ControllerBase
{
    private readonly IWorkerStateReader _workerStateReader;

    public WorkersController(IWorkerStateReader workerStateReader)
    {
        _workerStateReader = workerStateReader;
    }

    [HttpGet]
    [ProducesResponseType<WorkersSnapshotResponse>(StatusCodes.Status200OK)]
    public IActionResult GetWorkers()
    {
        var workers = _workerStateReader
            .GetWorkers()
            .OrderBy(x => x.WorkerId)
            .Select(x => new WorkerStateResponse(
                x.WorkerId,
                x.Name,
                x.ActiveJobCount,
                x.MaxConcurrentJobs,
                x.LastHeartbeatUtc,
                x.Status,
                x.GpuId,
                x.VramTotalMb,
                x.VramReservedMb,
                x.VramAvailableMb,
                x.SupportedModels,
                x.AssignedJobIds,
                x.CurrentModel,
                x.CurrentBatchSize,
                x.TotalBatchesFormed,
                x.LastBatchModel,
                x.LastBatchSize,
                x.LastBatchCompletedUtc,
                x.CompletedJobCount,
                x.FailedJobCount,
                x.JobUtilizationPercent,
                x.VramUtilizationPercent))
            .ToArray();

        var snapshot = new WorkersSnapshotResponse(
            _workerStateReader.GetSchedulerPolicy(),
            _workerStateReader.GetPendingJobCount(),
            workers);

        return Ok(snapshot);
    }
}
