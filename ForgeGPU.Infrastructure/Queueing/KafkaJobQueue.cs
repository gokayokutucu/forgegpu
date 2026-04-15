using Confluent.Kafka;
using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace ForgeGPU.Infrastructure.Queueing;

public sealed class KafkaJobQueue : IJobQueue, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly IProducer<string, string> _producer;
    private readonly IConsumer<string, string> _consumer;
    private readonly IReadOnlyDictionary<WeightBand, string> _topicsByBand;
    private readonly IReadOnlyDictionary<string, WeightBand> _bandByTopic;

    public KafkaJobQueue(IOptions<InfrastructureOptions> options)
    {
        _options = options.Value.Kafka;
        _topicsByBand = _options.GetTopicMap();
        _bandByTopic = _topicsByBand.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = _options.ProducerClientId,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

        _consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            ClientId = $"{_options.ConsumerGroupId}-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            AllowAutoCreateTopics = true
        }).Build();

        _consumer.Subscribe(_topicsByBand.Values);
    }

    public async ValueTask EnqueueAsync(Guid jobId, WeightBand weightBand, CancellationToken cancellationToken)
    {
        var topic = _options.GetTopicName(weightBand);
        await _producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = weightBand.ToString(),
                Value = jobId.ToString("D")
            },
            cancellationToken);
    }

    public ValueTask<JobIngressMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = _consumer.Consume(TimeSpan.FromMilliseconds(500));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                continue;
            }

            if (result is null)
            {
                continue;
            }

            var topic = result.Topic;

            if (!_bandByTopic.TryGetValue(topic, out var weightBand)
                && !Enum.TryParse<WeightBand>(result.Message.Key, ignoreCase: true, out weightBand))
            {
                throw new InvalidOperationException($"Kafka ingress topic '{topic}' is not mapped to a known weight band.");
            }

            if (!Guid.TryParse(result.Message.Value, out var jobId))
            {
                throw new InvalidOperationException($"Kafka ingress message on topic '{topic}' did not contain a valid job id.");
            }

            _consumer.Commit(result);
            return ValueTask.FromResult(new JobIngressMessage(jobId, weightBand, topic));
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public ValueTask<long?> GetIngressDepthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<long?>(null);
    }

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        _producer.Dispose();
    }
}
