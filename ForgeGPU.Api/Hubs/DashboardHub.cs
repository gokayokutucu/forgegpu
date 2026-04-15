using ForgeGPU.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace ForgeGPU.Api.Hubs;

public sealed class DashboardHub : Hub
{
    private readonly DashboardSnapshotBuilder _snapshotBuilder;
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(DashboardSnapshotBuilder snapshotBuilder, ILogger<DashboardHub> logger)
    {
        _snapshotBuilder = snapshotBuilder;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Dashboard SignalR client connected. ConnectionId: {ConnectionId}.", Context.ConnectionId);
        var snapshot = await _snapshotBuilder.BuildLiveUpdateAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("dashboardUpdate", snapshot, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Dashboard SignalR client disconnected. ConnectionId: {ConnectionId}.", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
