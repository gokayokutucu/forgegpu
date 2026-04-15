using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.IntegrationTests;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ApiFlowTests
{
    private readonly RuntimeDependenciesFixture _dependencies;

    public ApiFlowTests(RuntimeDependenciesFixture dependencies)
    {
        _dependencies = dependencies;
    }

    [Fact]
    public async Task JobSubmission_HappyPath_ShouldPersistAndComplete()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var submitted = await app.SubmitJobAsync("integration-happy-path", weight: 5);
        var completed = await app.WaitForJobAsync(submitted.Id, job => job.Status == JobStatus.Completed, TimeSpan.FromSeconds(10));

        (await app.JobExistsInDatabaseAsync(submitted.Id)).Should().BeTrue();
        completed.Weight.Should().Be(5);
        completed.WeightBand.Should().Be(WeightBand.W3_5);
        completed.Result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WeightBandVisibility_ShouldAppearInJobAndMetrics()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var submitted = await app.SubmitJobAsync("integration-weight-band", weight: 41);
        var completed = await app.WaitForJobAsync(submitted.Id, job => job.Status == JobStatus.Completed, TimeSpan.FromSeconds(10));
        var metrics = await app.GetJsonAsync<MetricsResponse>("/metrics");

        completed.Weight.Should().Be(41);
        completed.WeightBand.Should().Be(WeightBand.W41Plus);
        metrics.Should().NotBeNull();
        metrics!.Jobs.AcceptedByWeightBand["W41Plus"].Should().BeGreaterOrEqualTo(1);
        metrics.Jobs.CompletedByWeightBand["W41Plus"].Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task DeferredBehavior_ShouldRemainQueuedWhenNoMachineFits()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var submitted = await app.SubmitJobAsync(
            "integration-deferred",
            model: "gpt-sim-b",
            weight: 41,
            requiredMemoryMb: 20000);

        await Task.Delay(1000);
        var job = await app.GetJobAsync(submitted.Id);
        var metrics = await app.GetJsonAsync<MetricsResponse>("/metrics");

        job.Status.Should().Be(JobStatus.Queued);
        metrics.Should().NotBeNull();
        metrics!.Jobs.TotalDeferred.Should().BeGreaterThan(0);
        metrics.Scheduler.DeferralReasons.Should().ContainKey("NoEligibleMachine");
    }

    [Fact]
    public async Task ReliabilityFlows_ShouldRetryAndDeadLetterAsExpected()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var retryable = await app.SubmitJobAsync("fail-retry-once", weight: 10);
        var timeout = await app.SubmitJobAsync("slow-timeout", weight: 20);
        var terminal = await app.SubmitJobAsync("fail-always", weight: 21);

        var retryableJob = await app.WaitForJobAsync(retryable.Id, job => job.Status == JobStatus.Completed, TimeSpan.FromSeconds(10));
        var timeoutJob = await app.WaitForJobAsync(timeout.Id, job => job.Status == JobStatus.DeadLettered, TimeSpan.FromSeconds(10));
        var terminalJob = await app.WaitForJobAsync(terminal.Id, job => job.Status == JobStatus.DeadLettered, TimeSpan.FromSeconds(10));
        var metrics = await app.GetJsonAsync<MetricsResponse>("/metrics");

        retryableJob.RetryCount.Should().Be(1);
        timeoutJob.LastFailureCategory.Should().Be(JobFailureCategory.RetryExhausted);
        terminalJob.LastFailureCategory.Should().Be(JobFailureCategory.RetryExhausted);
        metrics.Should().NotBeNull();
        metrics!.Jobs.TotalRetried.Should().BeGreaterOrEqualTo(2);
        metrics.Jobs.DeadLetterCount.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task MachinesEndpoint_ShouldReturnDurableLiveAndAvailabilityShape()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var snapshot = await app.GetJsonAsync<MachinesSnapshotResponse>("/machines");

        snapshot.Should().NotBeNull();
        snapshot!.Machines.Should().HaveCountGreaterOrEqualTo(5);
        snapshot.Machines.Should().AllSatisfy(machine =>
        {
            machine.MachineId.Should().NotBeNullOrWhiteSpace();
            machine.Durable.Name.Should().NotBeNullOrWhiteSpace();
            machine.Live.ActorInstanceId.Should().NotBeNull();
            machine.Availability.LivenessState.Should().Be(ForgeGPU.Core.InferenceMachines.MachineLivenessState.Live);
        });
    }

    [Fact]
    public async Task DashboardRouteAndAssets_ShouldBeServed()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var dashboard = await app.Client.GetAsync("/dashboard/");
        var asset = await app.Client.GetAsync("/dashboard/app.js");

        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        asset.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EventsEndpoint_ShouldReturnRecentEventsAfterActivity()
    {
        await using var app = await ForgeGpuTestApp.StartAsync(_dependencies);

        var submitted = await app.SubmitJobAsync("integration-events", weight: 2);
        await app.WaitForJobAsync(submitted.Id, job => job.Status == JobStatus.Completed, TimeSpan.FromSeconds(10));

        var events = await app.GetJsonAsync<List<ForgeGPU.Core.Observability.OperationalEvent>>("/events/recent?limit=20");

        events.Should().NotBeNull();
        events!.Should().Contain(x => x.Kind == "JobDispatched");
        events.Should().Contain(x => x.Kind == "BandSelected");
    }
}
