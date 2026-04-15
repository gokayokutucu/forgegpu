using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ForgeGPU.IntegrationTests;

public sealed class RuntimeDependenciesFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("forgegpu_tests")
        .WithUsername("forgegpu")
        .WithPassword("forgegpu")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string PostgresConnectionString { get; private set; } = string.Empty;

    public string PostgresAdminConnectionString => new NpgsqlConnectionStringBuilder(PostgresConnectionString)
    {
        Database = "postgres"
    }.ConnectionString;

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        PostgresConnectionString = await ResolvePostgresConnectionStringAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> ResolvePostgresConnectionStringAsync()
    {
        var candidates = new[]
        {
            _postgres.GetConnectionString(),
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
            {
                Username = "postgres",
                Password = "postgres"
            }.ConnectionString
        };

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await using var connection = new NpgsqlConnection(candidate);
                await connection.OpenAsync();
                return candidate;
            }
            catch
            {
                // Try the next candidate.
            }
        }

        throw new InvalidOperationException("Could not establish a PostgreSQL test connection with the available credentials.");
    }
}
