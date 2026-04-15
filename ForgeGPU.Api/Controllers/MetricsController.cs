using ForgeGPU.Api.Contracts;
using ForgeGPU.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("metrics")]
public sealed class MetricsController : ControllerBase
{
    private readonly DashboardSnapshotBuilder _snapshotBuilder;

    public MetricsController(DashboardSnapshotBuilder snapshotBuilder)
    {
        _snapshotBuilder = snapshotBuilder;
    }

    [HttpGet]
    [ProducesResponseType<MetricsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken)
    {
        return Ok(await _snapshotBuilder.BuildMetricsAsync(cancellationToken));
    }
}
