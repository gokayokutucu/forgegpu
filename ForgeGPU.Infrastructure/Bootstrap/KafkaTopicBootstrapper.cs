using Confluent.Kafka;
using Confluent.Kafka.Admin;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeGPU.Infrastructure.Bootstrap;

public sealed class KafkaTopicBootstrapper : IHostedService
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaTopicBootstrapper> _logger;

    public KafkaTopicBootstrapper(IOptions<InfrastructureOptions> options, ILogger<KafkaTopicBootstrapper> logger)
    {
        _options = options.Value.Kafka;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _options.BootstrapServers
        }).Build();

        var topics = _options.GetTopicMap().Values
            .Distinct(StringComparer.Ordinal)
            .Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            })
            .ToArray();

        try
        {
            await adminClient.CreateTopicsAsync(topics, new CreateTopicsOptions());

            _logger.LogInformation(
                "Kafka ingress topics ensured. Topics: {Topics}.",
                string.Join(", ", topics.Select(x => x.Name)));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(x =>
            x.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
            _logger.LogInformation(
                "Kafka ingress topics already existed or were concurrently created. Topics: {Topics}.",
                string.Join(", ", topics.Select(x => x.Name)));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
