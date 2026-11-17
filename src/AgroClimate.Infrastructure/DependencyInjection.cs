using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Infrastructure.Analytics;
using AgroClimate.Infrastructure.Background;
using AgroClimate.Infrastructure.Persistence;
using AgroClimate.Infrastructure.Pipeline;
using AgroClimate.Infrastructure.Reporting;
using AgroClimate.Infrastructure.Seed;
using AgroClimate.Infrastructure.Services;
using AgroClimate.Infrastructure.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgroClimate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableBackgroundWorkers = true)
    {
        var connStr = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=IrGau_AgroClimateDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

        services.AddDbContext<AgroClimateDbContext>(options =>
            options.UseSqlServer(connStr));

        services.AddSingleton<ITelemetryChannel, TelemetryChannelBuffer>();
        services.AddSingleton<SimulatorController>();
        services.AddSingleton<ISimulatorController>(sp => sp.GetRequiredService<SimulatorController>());

        services.TryAddSingleton<IClimateRealtimeNotifier, NullClimateRealtimeNotifier>();

        services.AddScoped<ITelemetryAnalyticsService, DapperTelemetryAnalyticsService>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<IAgroProfileService, AgroProfileService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IPostmortemPdfService, PostmortemPdfService>();
        services.AddScoped<AgroClimateDataSeeder>();

        if (enableBackgroundWorkers)
        {
            services.AddHostedService<TelemetryBatchIngestionWorker>();
            services.AddHostedService<IoTSimulatorWorker>();
            services.AddHostedService<DeadMansSwitchWorker>();
        }

        return services;
    }

    private sealed class NullClimateRealtimeNotifier : IClimateRealtimeNotifier
    {
        public Task BroadcastZoneTelemetryAsync(ZoneLiveTelemetryDto telemetry, PipelineMetricsDto pipeline, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task BroadcastIncidentEventAsync(IncidentRealtimeEventDto incidentEvent, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task BroadcastSimulatorStatusAsync(SimulatorStatusDto status, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
