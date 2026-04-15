using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Configuration;
using ForgeGPU.Infrastructure.Queueing;
using ForgeGPU.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace ForgeGPU.IntegrationTests;

public sealed class ForgeGpuWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _postgresConnectionString;
    private readonly string _redisConnectionString;
    private readonly string _redisKeyPrefix;

    public ForgeGpuWebApplicationFactory(
        string postgresConnectionString,
        string redisConnectionString,
        string redisKeyPrefix)
    {
        _postgresConnectionString = postgresConnectionString;
        _redisConnectionString = redisConnectionString;
        _redisKeyPrefix = redisKeyPrefix;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.RemoveAll<IJobQueue>();
            services.RemoveAll<IJobStore>();
            services.RemoveAll<IOptions<InfrastructureOptions>>();

            services.AddSingleton(_ =>
            {
                var builder = new NpgsqlDataSourceBuilder(_postgresConnectionString);
                return builder.Build();
            });

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(_redisConnectionString));

            services.AddSingleton<IJobQueue, InMemoryJobQueue>();
            services.AddSingleton<IJobStore, PostgresJobStore>();

            services.AddSingleton<IOptions<InfrastructureOptions>>(_ =>
                Options.Create(new InfrastructureOptions
                {
                    Runtime = new RuntimeOptions
                    {
                        QueueProvider = "InMemory",
                        JobStoreProvider = "Postgres"
                    },
                    Postgres = new PostgresOptions
                    {
                        ConnectionString = _postgresConnectionString
                    },
                    Redis = new RedisOptions
                    {
                        ConnectionString = _redisConnectionString,
                        MachineProjectionKeyPrefix = _redisKeyPrefix
                    },
                    WorkerExecution = new WorkerExecutionOptions
                    {
                        WorkerCount = 5,
                        MaxConcurrentJobsPerWorker = 1,
                        HeartbeatIntervalSeconds = 1,
                        HeartbeatTtlSeconds = 3,
                        SchedulerPolicy = "ResourceAwareBestFit"
                    },
                    Scheduling = new SchedulingOptions
                    {
                        DefaultModel = "gpt-sim-a",
                        DefaultRequiredMemoryMb = 4096,
                        DeferRetryDelayMs = 50,
                        ModelDefaults =
                        [
                            new ModelMemoryDefault { Model = "gpt-sim-a", RequiredMemoryMb = 4096 },
                            new ModelMemoryDefault { Model = "gpt-sim-b", RequiredMemoryMb = 8192 },
                            new ModelMemoryDefault { Model = "gpt-sim-mix", RequiredMemoryMb = 6144 }
                        ]
                    },
                    Batching = new BatchingOptions
                    {
                        Enabled = false,
                        BatchWindowMs = 0,
                        MaxBatchSize = 1,
                        MaxBatchMemoryMb = 16384
                    },
                    Reliability = new ReliabilityOptions
                    {
                        MaxRetries = 1,
                        ExecutionTimeoutMs = 3000,
                        RetryDelayMs = 50
                    }
                }));
        });
    }
}
