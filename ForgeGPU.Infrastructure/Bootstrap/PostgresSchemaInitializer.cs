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
            "Ensuring Postgres schema for inference jobs and machine catalog. Connection: {ConnectionString}",
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

            CREATE TABLE IF NOT EXISTS machines (
                machine_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                total_capacity_units INTEGER NOT NULL,
                cpu_score INTEGER NOT NULL,
                ram_mb INTEGER NOT NULL,
                gpu_vram_mb INTEGER NOT NULL,
                max_parallel_workers INTEGER NOT NULL,
                supported_models TEXT[] NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                updated_at_utc TIMESTAMPTZ NOT NULL
            );

            ALTER TABLE machines
                ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE;

            CREATE INDEX IF NOT EXISTS ix_machines_enabled_machine_id
                ON machines (enabled, machine_id);
            """);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await SeedMachineCatalogAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task SeedMachineCatalogAsync(CancellationToken cancellationToken)
    {
        var definitions = ResolveSeedDefinitions();
        if (definitions.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var seededCount = 0;

        foreach (var machine in definitions)
        {
            await using var command = _dataSource.CreateCommand(
                """
                INSERT INTO machines (
                    machine_id,
                    name,
                    enabled,
                    total_capacity_units,
                    cpu_score,
                    ram_mb,
                    gpu_vram_mb,
                    max_parallel_workers,
                    supported_models,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    @machine_id,
                    @name,
                    @enabled,
                    @total_capacity_units,
                    @cpu_score,
                    @ram_mb,
                    @gpu_vram_mb,
                    @max_parallel_workers,
                    @supported_models,
                    @created_at_utc,
                    @updated_at_utc
                )
                ON CONFLICT (machine_id) DO NOTHING;
                """);

            command.Parameters.AddWithValue("machine_id", machine.MachineId);
            command.Parameters.AddWithValue("name", machine.Name);
            command.Parameters.AddWithValue("enabled", machine.Enabled);
            command.Parameters.AddWithValue("total_capacity_units", machine.TotalCapacityUnits);
            command.Parameters.AddWithValue("cpu_score", machine.CpuScore);
            command.Parameters.AddWithValue("ram_mb", machine.RamMb);
            command.Parameters.AddWithValue("gpu_vram_mb", machine.GpuVramMb);
            command.Parameters.AddWithValue("max_parallel_workers", machine.MaxParallelWorkers);
            command.Parameters.AddWithValue("supported_models", machine.SupportedModels.ToArray());
            command.Parameters.AddWithValue("created_at_utc", now);
            command.Parameters.AddWithValue("updated_at_utc", now);

            seededCount += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Machine catalog bootstrap completed. SeedDefinitions: {DefinitionCount}. Inserted: {InsertedCount}.",
            definitions.Count,
            seededCount);
    }

    private IReadOnlyCollection<MachineDefinition> ResolveSeedDefinitions()
    {
        if (_options.WorkerExecution.Machines.Count > 0)
        {
            return _options.WorkerExecution.Machines;
        }

        return
        [
            new MachineDefinition { MachineId = "machine-01", Name = "A100 Heavy Node", Enabled = true, TotalCapacityUnits = 15, CpuScore = 42, RamMb = 32768, GpuVramMb = 12288, MaxParallelWorkers = 2, SupportedModels = ["gpt-sim-a", "gpt-sim-mix"] },
            new MachineDefinition { MachineId = "machine-02", Name = "L4 Throughput Node", Enabled = true, TotalCapacityUnits = 20, CpuScore = 36, RamMb = 49152, GpuVramMb = 16384, MaxParallelWorkers = 3, SupportedModels = ["gpt-sim-b", "gpt-sim-mix"] },
            new MachineDefinition { MachineId = "machine-03", Name = "Balanced Multi-Model Node", Enabled = true, TotalCapacityUnits = 17, CpuScore = 40, RamMb = 65536, GpuVramMb = 14336, MaxParallelWorkers = 2, SupportedModels = ["gpt-sim-a", "gpt-sim-b", "gpt-sim-mix"] },
            new MachineDefinition { MachineId = "machine-04", Name = "Edge Constraint Node", Enabled = true, TotalCapacityUnits = 5, CpuScore = 16, RamMb = 16384, GpuVramMb = 4096, MaxParallelWorkers = 1, SupportedModels = ["gpt-sim-a"] },
            new MachineDefinition { MachineId = "machine-05", Name = "General Purpose Node", Enabled = true, TotalCapacityUnits = 12, CpuScore = 28, RamMb = 24576, GpuVramMb = 8192, MaxParallelWorkers = 2, SupportedModels = ["gpt-sim-b", "gpt-sim-mix"] }
        ];
    }
}
