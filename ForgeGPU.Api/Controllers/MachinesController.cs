using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceMachines;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("machines")]
public sealed class MachinesController : ControllerBase
{
    private readonly IMachineStateReader _machineStateReader;

    public MachinesController(IMachineStateReader machineStateReader)
    {
        _machineStateReader = machineStateReader;
    }

    [HttpGet]
    [ProducesResponseType<MachinesSnapshotResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMachines(CancellationToken cancellationToken)
    {
        var machines = (await _machineStateReader.GetMachinesAsync(cancellationToken))
            .OrderBy(x => x.MachineId)
            .Select(x => new MachineStateResponse(
                x.MachineId,
                new MachineMetadataResponse(
                    x.Name,
                    x.Enabled,
                    x.TotalCapacityUnits,
                    x.CpuScore,
                    x.RamMb,
                    x.GpuVramMb,
                    x.MaxParallelWorkers,
                    x.SupportedModels,
                    x.CreatedAtUtc,
                    x.UpdatedAtUtc),
                new MachineLiveStateResponse(
                    x.ActorInstanceId,
                    x.ActorStatus,
                    x.Status,
                    x.LastHeartbeatUtc,
                    x.UsedCapacityUnits,
                    x.RemainingCapacityUnits,
                    x.CapacityUtilizationPercent,
                    x.ActiveJobCount,
                    x.ParallelUtilizationPercent,
                    x.UsedGpuVramMb,
                    x.RemainingGpuVramMb,
                    x.GpuVramUtilizationPercent,
                    x.RunningJobIds,
                    x.CurrentModel,
                    x.CurrentBatchSize,
                    x.TotalBatchesFormed,
                    x.LastBatchModel,
                    x.LastBatchSize,
                    x.LastBatchCompletedUtc,
                    x.CompletedJobCount,
                    x.FailedJobCount),
                new MachineAvailabilityResponse(
                    x.LivenessState,
                    x.IsAvailableForScheduling)))
            .ToArray();

        return Ok(new MachinesSnapshotResponse(
            _machineStateReader.GetSchedulerPolicy(),
            _machineStateReader.GetPendingJobCount(),
            machines));
    }
}
