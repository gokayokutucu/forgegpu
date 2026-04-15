using ForgeGPU.Api.Contracts;
using ForgeGPU.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("machines")]
public sealed class MachinesController : ControllerBase
{
    private readonly DashboardSnapshotBuilder _snapshotBuilder;

    public MachinesController(DashboardSnapshotBuilder snapshotBuilder)
    {
        _snapshotBuilder = snapshotBuilder;
    }

    [HttpGet]
    [ProducesResponseType<MachinesSnapshotResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMachines(CancellationToken cancellationToken)
    {
        return Ok(await _snapshotBuilder.BuildMachinesAsync(cancellationToken));
    }
}
