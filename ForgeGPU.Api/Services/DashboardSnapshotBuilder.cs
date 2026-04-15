using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceMachines;
using ForgeGPU.Core.Observability;

namespace ForgeGPU.Api.Services;

public sealed class DashboardSnapshotBuilder
{
    private readonly IOrchestrationTelemetry _telemetry;
    private readonly IMachineStateReader _machineStateReader;

    public DashboardSnapshotBuilder(IOrchestrationTelemetry telemetry, IMachineStateReader machineStateReader)
    {
        _telemetry = telemetry;
        _machineStateReader = machineStateReader;
    }

    public async Task<MetricsResponse> BuildMetricsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _telemetry.GetSnapshotAsync(cancellationToken);

        return new MetricsResponse(
            snapshot.GeneratedAtUtc,
            new JobMetricsResponse(
                snapshot.Jobs.TotalAccepted,
                snapshot.Jobs.TotalCompleted,
                snapshot.Jobs.TotalFailed,
                snapshot.Jobs.TotalRetried,
                snapshot.Jobs.TotalTimedOut,
                snapshot.Jobs.TotalTerminalFailures,
                snapshot.Jobs.RetryExhaustedCount,
                snapshot.Jobs.DeadLetterCount,
                snapshot.Jobs.TotalDeferred,
                snapshot.Jobs.CurrentProcessing,
                snapshot.Jobs.AcceptedByWeightBand,
                snapshot.Jobs.CompletedByWeightBand,
                snapshot.Jobs.DeferredByWeightBand,
                snapshot.Jobs.CompletedByModel,
                snapshot.Jobs.FailuresByCategory),
            new QueueMetricsResponse(
                snapshot.Queue.IngressQueueDepth,
                snapshot.Queue.DeferredPendingCount,
                snapshot.Queue.IngressPublishedByTopic,
                snapshot.Queue.IngressConsumedByWeightBand,
                snapshot.Queue.IngressConsumedByTopic,
                snapshot.Queue.IngressLagByTopic,
                snapshot.Queue.CurrentBandBufferDepths,
                snapshot.Queue.PendingReasons),
            new SchedulerMetricsResponse(
                snapshot.Scheduler.Policy,
                snapshot.Scheduler.TotalDecisions,
                snapshot.Scheduler.SuccessfulDispatches,
                snapshot.Scheduler.Deferrals,
                snapshot.Scheduler.DispatchesByWeightBand,
                snapshot.Scheduler.BandCredits,
                snapshot.Scheduler.DeferralReasons),
            new BatchMetricsResponse(
                snapshot.Batching.TotalBatchesFormed,
                snapshot.Batching.AverageBatchSize,
                snapshot.Batching.RecentAverageBatchSize,
                snapshot.Batching.BatchesByModel),
            new WorkerUtilizationMetricsResponse(
                snapshot.Workers.TotalWorkers,
                snapshot.Workers.BusyWorkers,
                snapshot.Workers.SaturatedWorkers,
                snapshot.Workers.ActiveJobs,
                snapshot.Workers.JobCapacity,
                snapshot.Workers.JobUtilizationPercent,
                snapshot.Workers.ReservedVramMb,
                snapshot.Workers.TotalVramMb,
                snapshot.Workers.VramUtilizationPercent),
            new LatencyMetricsResponse(
                snapshot.Latency.Samples,
                snapshot.Latency.AverageQueueWaitMs,
                snapshot.Latency.AverageExecutionMs,
                snapshot.Latency.AverageTotalLatencyMs,
                snapshot.Latency.RecentAverageQueueWaitMs,
                snapshot.Latency.RecentAverageExecutionMs,
                snapshot.Latency.RecentAverageTotalLatencyMs));
    }

    public async Task<MachinesSnapshotResponse> BuildMachinesAsync(CancellationToken cancellationToken)
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

        return new MachinesSnapshotResponse(
            _machineStateReader.GetSchedulerPolicy(),
            _machineStateReader.GetPendingJobCount(),
            machines);
    }

    public IReadOnlyCollection<OperationalEvent> BuildRecentEvents(int limit = 100)
    {
        return _telemetry.GetRecentEvents(Math.Clamp(limit, 1, 200));
    }

    public async Task<DashboardLiveUpdateResponse> BuildLiveUpdateAsync(CancellationToken cancellationToken)
    {
        var metricsTask = BuildMetricsAsync(cancellationToken);
        var machinesTask = BuildMachinesAsync(cancellationToken);
        await Task.WhenAll(metricsTask, machinesTask);

        return new DashboardLiveUpdateResponse(
            DateTime.UtcNow,
            await metricsTask,
            await machinesTask,
            BuildRecentEvents(120));
    }
}
