using System.Text.Json.Serialization;
using ForgeGPU.Api.Hubs;
using ForgeGPU.Api.Services;
using ForgeGPU.Core.Observability;
using ForgeGPU.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddInferenceOrchestration(builder.Configuration);
builder.Services.AddSingleton<DashboardSnapshotBuilder>();
builder.Services.AddSingleton<IDashboardUpdateNotifier, SignalRDashboardUpdateNotifier>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/dashboard")
    {
        context.Response.Redirect("/dashboard/index.html");
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();

public partial class Program
{
}
