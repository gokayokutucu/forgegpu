using ForgeGPU.Core.InferenceJobs;
using StackExchange.Redis;

namespace ForgeGPU.Infrastructure.Queueing;

public sealed class RedisJobQueue : IJobQueue
{
    private readonly IConnectionMultiplexer _redis;
    private const string QueueName = "forgegpu:legacy:redis:jobs:queue";

    public RedisJobQueue(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async ValueTask EnqueueAsync(Guid jobId, WeightBand weightBand, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        await db.ListRightPushAsync(QueueName, $"{weightBand}:{jobId:D}");
    }

    public async ValueTask<JobIngressMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();

        while (!cancellationToken.IsCancellationRequested)
        {
            // BLPOP with small timeout keeps Redis wait efficient and still allows cooperative cancellation.
            var result = await db.ExecuteAsync("BLPOP", QueueName, "1");

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
            var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && Enum.TryParse<WeightBand>(parts[0], ignoreCase: true, out var weightBand)
                && Guid.TryParse(parts[1], out var jobId))
            {
                return new JobIngressMessage(jobId, weightBand, "redis-legacy");
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async ValueTask<long?> GetIngressDepthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        return await db.ListLengthAsync(QueueName);
    }
}
