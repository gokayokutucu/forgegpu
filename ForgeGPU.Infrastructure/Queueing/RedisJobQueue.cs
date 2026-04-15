using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ForgeGPU.Infrastructure.Queueing;

public sealed class RedisJobQueue : IJobQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _queueName;

    public RedisJobQueue(IConnectionMultiplexer redis, IOptions<InfrastructureOptions> options)
    {
        _redis = redis;
        _queueName = options.Value.Redis.QueueName;
    }

    public async ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        await db.ListRightPushAsync(_queueName, jobId.ToString("D"));
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();

        while (!cancellationToken.IsCancellationRequested)
        {
            // BLPOP with small timeout keeps Redis wait efficient and still allows cooperative cancellation.
            var result = await db.ExecuteAsync("BLPOP", _queueName, "1");

            if (result.IsNull)
            {
                continue;
            }

            var values = (RedisResult[]?)result;
            if (values is null || values.Length != 2)
            {
                continue;
            }

            var value = values[1].ToString();
            if (Guid.TryParse(value, out var jobId))
            {
                return jobId;
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
