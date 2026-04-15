using FluentAssertions;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.UnitTests.InferenceJobs;

public sealed class ReliabilityPolicyTests
{
    [Theory]
    [InlineData(JobFailureCategory.Timeout, true)]
    [InlineData(JobFailureCategory.ExecutionError, true)]
    [InlineData(JobFailureCategory.NonRetryableError, false)]
    [InlineData(JobFailureCategory.ValidationError, false)]
    [InlineData(JobFailureCategory.CapacityUnavailable, false)]
    [InlineData(JobFailureCategory.RetryExhausted, false)]
    public void IsRetryable_ShouldMatchCurrentPolicy(JobFailureCategory category, bool expected)
    {
        ReliabilityPolicy.IsRetryable(category).Should().Be(expected);
    }

    [Fact]
    public void ResolveTerminalCategory_ShouldReturnRetryExhausted_WhenRetryableCategoryCannotRetry()
    {
        var resolved = ReliabilityPolicy.ResolveTerminalCategory(JobFailureCategory.Timeout, canRetry: false);

        resolved.Should().Be(JobFailureCategory.RetryExhausted);
    }

    [Fact]
    public void InferenceJob_ShouldTrackTimeoutFailureAndRetryExhaustion()
    {
        var job = new InferenceJob("slow-timeout", "gpt-sim-a", maxRetries: 1);

        job.MarkRetrying("timed out", JobFailureCategory.Timeout, DateTime.UtcNow);

        job.RetryCount.Should().Be(1);
        job.Status.Should().Be(JobStatus.Retrying);
        job.LastFailureCategory.Should().Be(JobFailureCategory.Timeout);
        job.CanRetry().Should().BeFalse();
        ReliabilityPolicy.ResolveTerminalCategory(JobFailureCategory.Timeout, job.CanRetry())
            .Should()
            .Be(JobFailureCategory.RetryExhausted);
    }
}
