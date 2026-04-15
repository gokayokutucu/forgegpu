namespace ForgeGPU.Infrastructure.Configuration;

public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    public RuntimeOptions Runtime { get; init; } = new();
    public RedisOptions Redis { get; init; } = new();
    public PostgresOptions Postgres { get; init; } = new();
    public WorkerExecutionOptions WorkerExecution { get; init; } = new();
    public SchedulingOptions Scheduling { get; init; } = new();
    public BatchingOptions Batching { get; init; } = new();
    public ReliabilityOptions Reliability { get; init; } = new();
}

public sealed class RuntimeOptions
{
    public string QueueProvider { get; init; } = "Redis";
    public string JobStoreProvider { get; init; } = "Postgres";
}

public sealed class RedisOptions
{
    public string ConnectionString { get; init; } = "localhost:6379";
    public string QueueName { get; init; } = "forgegpu:inference:jobs:queue";
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; init; } = "Host=localhost;Port=5432;Database=forgegpu;Username=forgegpu;Password=forgegpu";
}

public sealed class WorkerExecutionOptions
{
    public int WorkerCount { get; init; } = 3;
    public int MaxConcurrentJobsPerWorker { get; init; } = 1;
    public int HeartbeatIntervalSeconds { get; init; } = 5;
    public string SchedulerPolicy { get; init; } = "LeastLoadedGpuAware";
    public IReadOnlyCollection<WorkerDefinition> Workers { get; init; } = [];
}

public sealed class SchedulingOptions
{
    public string DefaultModel { get; init; } = "gpt-sim-a";
    public int DefaultRequiredMemoryMb { get; init; } = 4096;
    public int DeferRetryDelayMs { get; init; } = 250;
    public IReadOnlyCollection<ModelMemoryDefault> ModelDefaults { get; init; } =
    [
        new() { Model = "gpt-sim-a", RequiredMemoryMb = 4096 },
        new() { Model = "gpt-sim-b", RequiredMemoryMb = 8192 },
        new() { Model = "gpt-sim-mix", RequiredMemoryMb = 6144 }
    ];
}

public sealed class BatchingOptions
{
    public bool Enabled { get; init; } = true;
    public int BatchWindowMs { get; init; } = 50;
    public int MaxBatchSize { get; init; } = 4;
    public int MaxBatchMemoryMb { get; init; } = 16384;
}

public sealed class ReliabilityOptions
{
    public int MaxRetries { get; init; } = 2;
    public int ExecutionTimeoutMs { get; init; } = 3000;
    public int RetryDelayMs { get; init; } = 500;
}

public sealed class WorkerDefinition
{
    public string WorkerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string GpuId { get; init; } = "GPU-0";
    public int VramTotalMb { get; init; } = 12288;
    public int MaxConcurrentJobs { get; init; } = 1;
    public IReadOnlyCollection<string> SupportedModels { get; init; } = [];
}

public sealed class ModelMemoryDefault
{
    public string Model { get; init; } = string.Empty;
    public int RequiredMemoryMb { get; init; } = 4096;
}
