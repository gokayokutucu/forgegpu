using ForgeGPU.Core.InferenceJobs;
using ForgeGPU.Infrastructure.Configuration;
using ForgeGPU.Infrastructure.Queueing;
using ForgeGPU.Infrastructure.Storage;
using ForgeGPU.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeGPU.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInferenceOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<InfrastructureOptions>()
            .Bind(configuration.GetSection(InfrastructureOptions.SectionName));

        services.AddSingleton<IJobQueue, InMemoryJobQueue>();
        services.AddSingleton<IJobStore, InMemoryJobStore>();
        services.AddHostedService<InferenceWorker>();

        return services;
    }
}
