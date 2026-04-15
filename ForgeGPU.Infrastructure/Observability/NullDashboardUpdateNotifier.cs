namespace ForgeGPU.Infrastructure.Observability;

using ForgeGPU.Core.Observability;

public sealed class NullDashboardUpdateNotifier : IDashboardUpdateNotifier
{
    public void NotifyStateChanged()
    {
    }
}
