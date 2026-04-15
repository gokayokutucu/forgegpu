using ForgeGPU.Core.Observability;
using ForgeGPU.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("events")]
public sealed class EventsController : ControllerBase
{
    private readonly DashboardSnapshotBuilder _snapshotBuilder;

    public EventsController(DashboardSnapshotBuilder snapshotBuilder)
    {
        _snapshotBuilder = snapshotBuilder;
    }

    [HttpGet("recent")]
    [ProducesResponseType<IReadOnlyCollection<OperationalEvent>>(StatusCodes.Status200OK)]
    public IActionResult GetRecent([FromQuery] int limit = 100)
    {
        return Ok(_snapshotBuilder.BuildRecentEvents(limit));
    }
}
