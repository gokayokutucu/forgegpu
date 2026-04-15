using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Core.InferenceMachines;
using ForgeGPU.Core.InferenceWorkers;
using ForgeGPU.Core.Observability;
using ForgeGPU.Infrastructure.Bootstrap;
using ForgeGPU.Infrastructure.Configuration;
using ForgeGPU.Infrastructure.Observability;
using ForgeGPU.Infrastructure.Projection;
using ForgeGPU.Infrastructure.Queueing;
using ForgeGPU.Infrastructure.Scheduling;
using ForgeGPU.Infrastructure.Storage;
using ForgeGPU.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StackExchange.Redis;

namespace ForgeGPU.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInferenceOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<InfrastructureOptions>()
            .Bind(configuration.GetSection(InfrastructureOptions.SectionName));

        var options = configuration
            .GetSection(InfrastructureOptions.SectionName)
            .Get<InfrastructureOptions>() ?? new InfrastructureOptions();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(options.Redis.ConnectionString));

        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(options.Postgres.ConnectionString);
            return builder.Build();
        });

        if (IsProvider(options.Runtime.QueueProvider, "Kafka"))
        {
            services.AddSingleton<IJobQueue, KafkaJobQueue>();
        }
        else if (IsProvider(options.Runtime.QueueProvider, "Redis"))
        {
            services.AddSingleton<IJobQueue, RedisJobQueue>();
        }
        else
        {
            services.AddSingleton<IJobQueue, InMemoryJobQueue>();
        }

        if (IsProvider(options.Runtime.JobStoreProvider, "Postgres"))
        {
            services.AddSingleton<IJobStore, PostgresJobStore>();
        }
        else
        {
            services.AddSingleton<IJobStore, InMemoryJobStore>();
        }

        services.AddSingleton<IMachineCatalogStore, PostgresMachineCatalogStore>();
        services.AddSingleton<IMachineLiveProjectionStore, RedisMachineLiveProjectionStore>();
        services.AddSingleton<IResourceEstimator, ResourceEstimator>();
        services.AddSingleton<IMachineScheduler, ResourceAwareMachineScheduler>();
        services.TryAddSingleton<IDashboardUpdateNotifier, NullDashboardUpdateNotifier>();
        services.AddSingleton<InferenceOrchestrationService>();
        services.AddSingleton<IWorkerStateReader>(sp => sp.GetRequiredService<InferenceOrchestrationService>());
        services.AddSingleton<IMachineStateReader>(sp => sp.GetRequiredService<InferenceOrchestrationService>());
        services.AddSingleton<IOrchestrationTelemetry>(sp => sp.GetRequiredService<InferenceOrchestrationService>());

        services.AddHostedService<PostgresSchemaInitializer>();
        if (IsProvider(options.Runtime.QueueProvider, "Kafka"))
        {
            services.AddHostedService<KafkaTopicBootstrapper>();
        }
        services.AddHostedService(sp => sp.GetRequiredService<InferenceOrchestrationService>());

        return services;
    }

    private static bool IsProvider(string configuredProvider, string expectedProvider)
    {
        return string.Equals(configuredProvider, expectedProvider, StringComparison.OrdinalIgnoreCase);
    }
}
