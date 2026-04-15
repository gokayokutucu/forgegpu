using System.Threading.Channels;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Infrastructure.Queueing;

public sealed class InMemoryJobQueue : IJobQueue
{
    private readonly Channel<JobIngressMessage> _channel = Channel.CreateUnbounded<JobIngressMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public async ValueTask EnqueueAsync(Guid jobId, WeightBand weightBand, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(new JobIngressMessage(jobId, weightBand, "in-memory"), cancellationToken);
    }

    public async ValueTask<JobIngressMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }

    public ValueTask<long?> GetIngressDepthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<long?>(null);
    }
}
