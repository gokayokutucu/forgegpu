using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ForgeGPU.Infrastructure.Bootstrap;

public sealed class PostgresSchemaInitializer : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresSchemaInitializer> _logger;
    private readonly InfrastructureOptions _options;

    public PostgresSchemaInitializer(
        NpgsqlDataSource dataSource,
        IOptions<InfrastructureOptions> options,
        ILogger<PostgresSchemaInitializer> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Ensuring Postgres schema for inference jobs. Connection: {ConnectionString}",
            _options.Postgres.ConnectionString);

        await using var command = _dataSource.CreateCommand(
            """
            CREATE TABLE IF NOT EXISTS inference_jobs (
                id UUID PRIMARY KEY,
                prompt TEXT NOT NULL,
                model TEXT NULL,
                weight INTEGER NOT NULL DEFAULT 100,
                required_memory_mb INTEGER NOT NULL DEFAULT 4096,
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 2,
                last_failure_reason TEXT NULL,
                last_failure_category TEXT NULL,
                last_attempt_at_utc TIMESTAMPTZ NULL,
                status TEXT NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                started_at_utc TIMESTAMPTZ NULL,
                completed_at_utc TIMESTAMPTZ NULL,
                result TEXT NULL,
                error TEXT NULL
            );

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS weight INTEGER NOT NULL DEFAULT 100;

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS required_memory_mb INTEGER NOT NULL DEFAULT 4096;

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS retry_count INTEGER NOT NULL DEFAULT 0;

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS max_retries INTEGER NOT NULL DEFAULT 2;

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS last_failure_reason TEXT NULL;

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS last_failure_category TEXT NULL;

            ALTER TABLE inference_jobs
                ADD COLUMN IF NOT EXISTS last_attempt_at_utc TIMESTAMPTZ NULL;

            CREATE INDEX IF NOT EXISTS ix_inference_jobs_status_created_at
                ON inference_jobs (status, created_at_utc);
            """);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
