using System.Threading.Channels;
using ForgeGPU.Core.InferenceJobs;

namespace ForgeGPU.Infrastructure.Queueing;

public sealed class InMemoryJobQueue : IJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public async ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(jobId, cancellationToken);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
