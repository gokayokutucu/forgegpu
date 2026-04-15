using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ForgeGPU.Api.Contracts;
using ForgeGPU.Core.InferenceJobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace ForgeGPU.IntegrationTests;

public sealed class ForgeGpuTestApp : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RuntimeDependenciesFixture _dependencies;
    private readonly string _databaseName;

    public ForgeGpuWebApplicationFactory Factory { get; }
    public HttpClient Client { get; }
    public string DatabaseConnectionString { get; }

    private ForgeGpuTestApp(
        RuntimeDependenciesFixture dependencies,
        string databaseName,
        string databaseConnectionString,
        ForgeGpuWebApplicationFactory factory,
        HttpClient client)
    {
        _dependencies = dependencies;
        _databaseName = databaseName;
        DatabaseConnectionString = databaseConnectionString;
        Factory = factory;
        Client = client;
    }

    public static async Task<ForgeGpuTestApp> StartAsync(RuntimeDependenciesFixture dependencies)
    {
        var databaseName = $"forgegpu_test_{Guid.NewGuid():N}";
        var redisPrefix = $"forgegpu:test:{Guid.NewGuid():N}:";

        await CreateDatabaseAsync(dependencies.PostgresAdminConnectionString, databaseName);

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(dependencies.PostgresAdminConnectionString)
        {
            Database = databaseName
        };

        var factory = new ForgeGpuWebApplicationFactory(
            connectionStringBuilder.ConnectionString,
            dependencies.RedisConnectionString,
            redisPrefix);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await WaitForHealthyAsync(client, TimeSpan.FromSeconds(20));

        return new ForgeGpuTestApp(dependencies, databaseName, connectionStringBuilder.ConnectionString, factory, client);
    }

    public async Task<SubmitJobResponse> SubmitJobAsync(string prompt, string model = "gpt-sim-a", int weight = 100, int? requiredMemoryMb = 2048)
    {
        var response = await Client.PostAsJsonAsync("/jobs", new
        {
            prompt,
            model,
            weight,
            requiredMemoryMb
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SubmitJobResponse>(JsonOptions);
        body.Should().NotBeNull();
        return body!;
    }

    public async Task<JobDetailsResponse> GetJobAsync(Guid id)
    {
        var response = await Client.GetAsync($"/jobs/{id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(JsonOptions);
        body.Should().NotBeNull();
        return body!;
    }

    public async Task<T?> GetJsonAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task<JobDetailsResponse> WaitForJobAsync(Guid id, Func<JobDetailsResponse, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        JobDetailsResponse? last = null;

        while (DateTime.UtcNow < deadline)
        {
            last = await GetJobAsync(id);
            if (predicate(last))
            {
                return last;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Job {id} did not reach expected state. Last status: {last?.Status}.");
    }

    public async Task<bool> JobExistsInDatabaseAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM inference_jobs WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count == 1;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await DropDatabaseAsync(_dependencies.PostgresAdminConnectionString, _databaseName);
    }

    private static async Task WaitForHealthyAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Retry until timeout.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Application test host did not become healthy in time.");
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        var terminateSql =
            $"""
             SELECT pg_terminate_backend(pid)
             FROM pg_stat_activity
             WHERE datname = '{databaseName}'
               AND pid <> pg_backend_pid();
             """;
        await using (var terminate = new NpgsqlCommand(terminateSql, connection))
        {
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\";", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
