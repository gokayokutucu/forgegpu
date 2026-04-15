using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.Observability;
using Microsoft.AspNetCore.Mvc;

namespace ForgeGPU.Api.Controllers;

[ApiController]
[Route("metrics")]
public sealed class MetricsController : ControllerBase
{
    private readonly IOrchestrationTelemetry _telemetry;

    public MetricsController(IOrchestrationTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    [HttpGet]
    [ProducesResponseType<MetricsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken)
    {
        var snapshot = await _telemetry.GetSnapshotAsync(cancellationToken);

        var response = new MetricsResponse(
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
                snapshot.Jobs.CompletedByModel,
                snapshot.Jobs.FailuresByCategory),
            new QueueMetricsResponse(
                snapshot.Queue.IngressQueueDepth,
                snapshot.Queue.DeferredPendingCount,
                snapshot.Queue.PendingReasons),
            new SchedulerMetricsResponse(
                snapshot.Scheduler.Policy,
                snapshot.Scheduler.TotalDecisions,
                snapshot.Scheduler.SuccessfulDispatches,
                snapshot.Scheduler.Deferrals,
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

        return Ok(response);
    }
}
