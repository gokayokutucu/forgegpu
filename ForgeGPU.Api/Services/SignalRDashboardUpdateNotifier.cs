using ForgeGPU.Api.Hubs;
using ForgeGPU.Core.Observability;
using Microsoft.AspNetCore.SignalR;

namespace ForgeGPU.Api.Services;

public sealed class SignalRDashboardUpdateNotifier : IDashboardUpdateNotifier
{
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SignalRDashboardUpdateNotifier> _logger;
    private int _broadcastScheduled;

    public SignalRDashboardUpdateNotifier(
        IHubContext<DashboardHub> hubContext,
        IServiceProvider serviceProvider,
        ILogger<SignalRDashboardUpdateNotifier> logger)
    {
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void NotifyStateChanged()
    {
        if (Interlocked.Exchange(ref _broadcastScheduled, 1) == 1)
        {
            return;
        }

        _ = BroadcastSoonAsync();
    }

    private async Task BroadcastSoonAsync()
    {
        try
        {
            await Task.Delay(250);
            var snapshotBuilder = _serviceProvider.GetRequiredService<DashboardSnapshotBuilder>();
            var snapshot = await snapshotBuilder.BuildLiveUpdateAsync(CancellationToken.None);
            await _hubContext.Clients.All.SendAsync("dashboardUpdate", snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dashboard SignalR broadcast skipped.");
        }
        finally
        {
            Interlocked.Exchange(ref _broadcastScheduled, 0);
        }
    }
}
