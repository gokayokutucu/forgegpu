using ForgeGPU.Core.InferenceMachines;
using Npgsql;

namespace ForgeGPU.Infrastructure.Storage;

public sealed class PostgresMachineCatalogStore : IMachineCatalogStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresMachineCatalogStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyCollection<MachineCatalogEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT
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
            FROM machines
            ORDER BY machine_id;
            """);

        var results = new List<MachineCatalogEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MachineCatalogEntry(
                reader.GetString(reader.GetOrdinal("machine_id")),
                reader.GetString(reader.GetOrdinal("name")),
                reader.GetBoolean(reader.GetOrdinal("enabled")),
                reader.GetInt32(reader.GetOrdinal("total_capacity_units")),
                reader.GetInt32(reader.GetOrdinal("cpu_score")),
                reader.GetInt32(reader.GetOrdinal("ram_mb")),
                reader.GetInt32(reader.GetOrdinal("gpu_vram_mb")),
                reader.GetInt32(reader.GetOrdinal("max_parallel_workers")),
                reader.GetFieldValue<string[]>(reader.GetOrdinal("supported_models")),
                reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
                reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))));
        }

        return results;
    }
}
