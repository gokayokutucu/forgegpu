using System.Text.Json;
using ForgeGPU.Core.InferenceMachines;
using ForgeGPU.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ForgeGPU.Infrastructure.Projection;

public sealed class RedisMachineLiveProjectionStore : IMachineLiveProjectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisMachineLiveProjectionStore(IConnectionMultiplexer redis, IOptions<InfrastructureOptions> options)
    {
        _redis = redis;
        _keyPrefix = options.Value.Redis.MachineProjectionKeyPrefix;
    }

    public async ValueTask PublishAsync(MachineLiveProjection projection, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var key = BuildKey(projection.MachineId);

        await db.HashSetAsync(key,
        [
            new HashEntry("actor_instance_id", projection.ActorInstanceId),
            new HashEntry("machine_id", projection.MachineId),
            new HashEntry("actor_status", projection.ActorStatus.ToString()),
            new HashEntry("runtime_status", projection.RuntimeStatus.ToString()),
            new HashEntry("last_heartbeat_utc", projection.LastHeartbeatUtc.ToString("O")),
            new HashEntry("used_capacity_units", projection.UsedCapacityUnits),
            new HashEntry("remaining_capacity_units", projection.RemainingCapacityUnits),
            new HashEntry("active_job_count", projection.ActiveJobCount),
            new HashEntry("reserved_vram_mb", projection.ReservedVramMb),
            new HashEntry("running_job_ids", JsonSerializer.Serialize(projection.RunningJobIds, JsonOptions)),
            new HashEntry("current_batch_size", projection.CurrentBatchSize),
            new HashEntry("current_model", projection.CurrentModel ?? string.Empty)
        ]);

        await db.KeyExpireAsync(key, ttl);
    }

    public ValueTask PublishOfflineAsync(MachineLiveProjection projection, TimeSpan ttl, CancellationToken cancellationToken)
    {
        return PublishAsync(projection with { ActorStatus = MachineActorStatus.Offline }, ttl, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, MachineLiveProjection>> GetManyAsync(
        IReadOnlyCollection<string> machineIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        var results = new Dictionary<string, MachineLiveProjection>(StringComparer.Ordinal);

        foreach (var machineId in machineIds)
        {
            var entries = await db.HashGetAllAsync(BuildKey(machineId));
            if (entries.Length == 0)
            {
                continue;
            }

            var values = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var runningJobs = values.TryGetValue("running_job_ids", out var runningJobIdsRaw) && !string.IsNullOrWhiteSpace(runningJobIdsRaw)
                ? JsonSerializer.Deserialize<Guid[]>(runningJobIdsRaw, JsonOptions) ?? []
                : [];

            results[machineId] = new MachineLiveProjection(
                machineId,
                values.GetValueOrDefault("actor_instance_id") ?? string.Empty,
                Enum.TryParse<MachineActorStatus>(values.GetValueOrDefault("actor_status"), true, out var actorStatus)
                    ? actorStatus
                    : MachineActorStatus.Starting,
                DateTime.TryParse(values.GetValueOrDefault("last_heartbeat_utc"), out var lastHeartbeatUtc)
                    ? DateTime.SpecifyKind(lastHeartbeatUtc, DateTimeKind.Utc)
                    : DateTime.MinValue,
                int.TryParse(values.GetValueOrDefault("used_capacity_units"), out var usedCapacityUnits)
                    ? usedCapacityUnits
                    : 0,
                int.TryParse(values.GetValueOrDefault("remaining_capacity_units"), out var remainingCapacityUnits)
                    ? remainingCapacityUnits
                    : 0,
                int.TryParse(values.GetValueOrDefault("active_job_count"), out var activeJobCount)
                    ? activeJobCount
                    : 0,
                int.TryParse(values.GetValueOrDefault("reserved_vram_mb"), out var reservedVramMb)
                    ? reservedVramMb
                    : 0,
                runningJobs,
                int.TryParse(values.GetValueOrDefault("current_batch_size"), out var currentBatchSize)
                    ? currentBatchSize
                    : 0,
                string.IsNullOrWhiteSpace(values.GetValueOrDefault("current_model"))
                    ? null
                    : values["current_model"],
                Enum.TryParse<MachineStatus>(values.GetValueOrDefault("runtime_status"), true, out var runtimeStatus)
                    ? runtimeStatus
                    : MachineStatus.Idle);
        }

        return results;
    }

    private string BuildKey(string machineId) => $"{_keyPrefix}{machineId}";
}
