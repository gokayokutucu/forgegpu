namespace ForgeGPU.Infrastructure.Configuration;

public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    public RuntimeOptions Runtime { get; init; } = new();
    public RedisOptions Redis { get; init; } = new();
    public PostgresOptions Postgres { get; init; } = new();
}

public sealed class RuntimeOptions
{
    public string QueueProvider { get; init; } = "InMemory";
    public string JobStoreProvider { get; init; } = "InMemory";
}

public sealed class RedisOptions
{
    public string ConnectionString { get; init; } = "localhost:6379";
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; init; } = "Host=localhost;Port=5432;Database=forgegpu;Username=forgegpu;Password=forgegpu";
}
