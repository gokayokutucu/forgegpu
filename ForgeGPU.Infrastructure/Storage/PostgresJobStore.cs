using ForgeGPU.Core.InferenceJobs;
using Npgsql;

namespace ForgeGPU.Infrastructure.Storage;

public sealed class PostgresJobStore : IJobStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresJobStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task AddAsync(InferenceJob job, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO inference_jobs (
                id,
                prompt,
                model,
                weight,
                required_memory_mb,
                retry_count,
                max_retries,
                last_failure_reason,
                last_failure_category,
                last_attempt_at_utc,
                status,
                created_at_utc,
                started_at_utc,
                completed_at_utc,
                result,
                error
            )
            VALUES (
                @id,
                @prompt,
                @model,
                @weight,
                @required_memory_mb,
                @retry_count,
                @max_retries,
                @last_failure_reason,
                @last_failure_category,
                @last_attempt_at_utc,
                @status,
                @created_at_utc,
                @started_at_utc,
                @completed_at_utc,
                @result,
                @error
            );
            """);

        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("prompt", job.Prompt);
        command.Parameters.AddWithValue("model", (object?)job.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("weight", job.Weight);
        command.Parameters.AddWithValue("required_memory_mb", job.RequiredMemoryMb);
        command.Parameters.AddWithValue("retry_count", job.RetryCount);
        command.Parameters.AddWithValue("max_retries", job.MaxRetries);
        command.Parameters.AddWithValue("last_failure_reason", (object?)job.LastFailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("last_failure_category", (object?)job.LastFailureCategory?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("last_attempt_at_utc", (object?)job.LastAttemptAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("status", job.Status.ToString());
        command.Parameters.AddWithValue("created_at_utc", job.CreatedAtUtc);
        command.Parameters.AddWithValue("started_at_utc", (object?)job.StartedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("completed_at_utc", (object?)job.CompletedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("result", (object?)job.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)job.Error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InferenceJob?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT
                id,
                prompt,
                model,
                weight,
                required_memory_mb,
                retry_count,
                max_retries,
                last_failure_reason,
                last_failure_category,
                last_attempt_at_utc,
                status,
                created_at_utc,
                started_at_utc,
                completed_at_utc,
                result,
                error
            FROM inference_jobs
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var statusText = reader.GetString(reader.GetOrdinal("status"));
        var status = Enum.Parse<JobStatus>(statusText, ignoreCase: true);

        return InferenceJob.Rehydrate(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("prompt")),
            reader.IsDBNull(reader.GetOrdinal("model")) ? null : reader.GetString(reader.GetOrdinal("model")),
            reader.GetInt32(reader.GetOrdinal("weight")),
            reader.GetInt32(reader.GetOrdinal("required_memory_mb")),
            reader.GetInt32(reader.GetOrdinal("retry_count")),
            reader.GetInt32(reader.GetOrdinal("max_retries")),
            reader.IsDBNull(reader.GetOrdinal("last_failure_reason")) ? null : reader.GetString(reader.GetOrdinal("last_failure_reason")),
            reader.IsDBNull(reader.GetOrdinal("last_failure_category"))
                ? null
                : Enum.Parse<JobFailureCategory>(reader.GetString(reader.GetOrdinal("last_failure_category")), ignoreCase: true),
            reader.IsDBNull(reader.GetOrdinal("last_attempt_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("last_attempt_at_utc")),
            status,
            reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            reader.IsDBNull(reader.GetOrdinal("started_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("started_at_utc")),
            reader.IsDBNull(reader.GetOrdinal("completed_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("completed_at_utc")),
            reader.IsDBNull(reader.GetOrdinal("result")) ? null : reader.GetString(reader.GetOrdinal("result")),
            reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error")));
    }

    public async Task UpdateAsync(InferenceJob job, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE inference_jobs
            SET
                status = @status,
                started_at_utc = @started_at_utc,
                completed_at_utc = @completed_at_utc,
                retry_count = @retry_count,
                max_retries = @max_retries,
                last_failure_reason = @last_failure_reason,
                last_failure_category = @last_failure_category,
                last_attempt_at_utc = @last_attempt_at_utc,
                result = @result,
                error = @error
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("status", job.Status.ToString());
        command.Parameters.AddWithValue("started_at_utc", (object?)job.StartedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("completed_at_utc", (object?)job.CompletedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("retry_count", job.RetryCount);
        command.Parameters.AddWithValue("max_retries", job.MaxRetries);
        command.Parameters.AddWithValue("last_failure_reason", (object?)job.LastFailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("last_failure_category", (object?)job.LastFailureCategory?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("last_attempt_at_utc", (object?)job.LastAttemptAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("result", (object?)job.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)job.Error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<InferenceJob>> ListDeadLetteredAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT
                id,
                prompt,
                model,
                weight,
                required_memory_mb,
                retry_count,
                max_retries,
                last_failure_reason,
                last_failure_category,
                last_attempt_at_utc,
                status,
                created_at_utc,
                started_at_utc,
                completed_at_utc,
                result,
                error
            FROM inference_jobs
            WHERE status = @status
            ORDER BY completed_at_utc DESC NULLS LAST, created_at_utc DESC
            LIMIT @limit;
            """);

        command.Parameters.AddWithValue("status", JobStatus.DeadLettered.ToString());
        command.Parameters.AddWithValue("limit", Math.Max(1, limit));

        var jobs = new List<InferenceJob>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var statusText = reader.GetString(reader.GetOrdinal("status"));
            var status = Enum.Parse<JobStatus>(statusText, ignoreCase: true);

            jobs.Add(InferenceJob.Rehydrate(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("prompt")),
                reader.IsDBNull(reader.GetOrdinal("model")) ? null : reader.GetString(reader.GetOrdinal("model")),
                reader.GetInt32(reader.GetOrdinal("weight")),
                reader.GetInt32(reader.GetOrdinal("required_memory_mb")),
                reader.GetInt32(reader.GetOrdinal("retry_count")),
                reader.GetInt32(reader.GetOrdinal("max_retries")),
                reader.IsDBNull(reader.GetOrdinal("last_failure_reason")) ? null : reader.GetString(reader.GetOrdinal("last_failure_reason")),
                reader.IsDBNull(reader.GetOrdinal("last_failure_category"))
                    ? null
                    : Enum.Parse<JobFailureCategory>(reader.GetString(reader.GetOrdinal("last_failure_category")), ignoreCase: true),
                reader.IsDBNull(reader.GetOrdinal("last_attempt_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("last_attempt_at_utc")),
                status,
                reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
                reader.IsDBNull(reader.GetOrdinal("started_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("started_at_utc")),
                reader.IsDBNull(reader.GetOrdinal("completed_at_utc")) ? null : reader.GetDateTime(reader.GetOrdinal("completed_at_utc")),
                reader.IsDBNull(reader.GetOrdinal("result")) ? null : reader.GetString(reader.GetOrdinal("result")),
                reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error"))));
        }

        return jobs;
    }
}
