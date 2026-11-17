using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Enums;
using AgroClimate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgroClimate.Infrastructure.Background;

/// <summary>
/// Фоновый сторожевой детектор потери сигнала телеметрии (Dead Man's Switch) на базе PeriodicTimer.
/// Отслеживает зависание микроконтроллеров и обрывы линии связи RS-485 / Wi-Fi.
/// </summary>
public sealed class DeadMansSwitchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISimulatorController _simulator;
    private readonly ITelemetryChannel _channel;
    private readonly IClimateRealtimeNotifier _notifier;
    private readonly ILogger<DeadMansSwitchWorker> _logger;

    public DeadMansSwitchWorker(
        IServiceScopeFactory scopeFactory,
        ISimulatorController simulator,
        ITelemetryChannel channel,
        IClimateRealtimeNotifier notifier,
        ILogger<DeadMansSwitchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _simulator = simulator;
        _channel = channel;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запущен сторожевой монитор Dead Man's Switch (PeriodicTimer = 2 с).");

        // Даем приложению и сидеру завершить стартовую инициализацию
        await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (!_simulator.GetStatus().IsRunning)
                    continue;

                await CheckHeartbeatsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле проверки Dead Man's Switch.");
            }
        }
    }

    public async Task CheckHeartbeatsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroClimateDbContext>();
        var incidentService = scope.ServiceProvider.GetRequiredService<IIncidentService>();

        var now = DateTime.UtcNow;
        var zones = await db.ClimateZones
            .Include(z => z.AgroProfile)
            .ToListAsync(ct);

        bool anyUpdated = false;

        foreach (var zone in zones)
        {
            if (zone.AgroProfile is null || !zone.LastTelemetryUtc.HasValue)
                continue;

            double silenceSec = (now - zone.LastTelemetryUtc.Value).TotalSeconds;
            if (silenceSec <= zone.HeartbeatTimeoutSeconds)
                continue;

            string msg = $"Потеря сигнала от контроллера {zone.ControllerNodeId}: нет пакетов {silenceSec:F0} с (таймаут {zone.HeartbeatTimeoutSeconds} с). Возможен обрыв RS-485/питания.";

            bool justWentOffline = zone.IsSensorOnline;
            zone.IsSensorOnline = false;
            zone.CurrentComplianceState = ComplianceState.Offline;
            zone.StatusNote = msg;
            anyUpdated = true;

            int blindFlightMinutes = Math.Max(10, (int)Math.Round(zone.AgroProfile.BaseTimeToDamageMinutes * 0.6));

            var incident = await incidentService.EnsureOrEscalateIncidentAsync(
                zone: zone,
                profile: zone.AgroProfile,
                severity: IncidentSeverity.Emergency,
                triggerType: IncidentTriggerType.DeadMansSwitch,
                summary: msg,
                tempC: zone.CurrentTempC,
                humidityPercent: zone.CurrentHumidityPercent,
                co2Ppm: zone.CurrentCo2Ppm,
                dewPointC: zone.CurrentDewPointC,
                vpdKpa: zone.CurrentVpdKpa,
                estimatedMinutesToDamage: blindFlightMinutes,
                ct: ct);

            if (justWentOffline)
            {
                _logger.LogWarning("DEAD MAN'S SWITCH сработал по объекту {ZoneName} ({Controller}).", zone.Name, zone.ControllerNodeId);
            }

            var liveDto = new ZoneLiveTelemetryDto(
                ZoneId: zone.Id,
                ZoneCode: zone.Code,
                ZoneName: zone.Name,
                CropName: zone.AgroProfile.CropName,
                RecordedAtUtc: zone.LastTelemetryUtc.Value,
                TemperatureC: zone.CurrentTempC,
                HumidityPercent: zone.CurrentHumidityPercent,
                Co2Ppm: zone.CurrentCo2Ppm,
                DewPointC: zone.CurrentDewPointC,
                VpdKpa: zone.CurrentVpdKpa,
                TempVelocityPerMin: 0.0,
                ComplianceState: ComplianceState.Offline.ToString(),
                StatusNote: msg,
                IsSensorOnline: false,
                ActiveIncidentId: incident.Id,
                ActiveIncidentSeverity: incident.Severity.ToString(),
                GraceRemainingSeconds: null);

            await _notifier.BroadcastZoneTelemetryAsync(liveDto, _channel.GetMetrics(), ct);
        }

        if (anyUpdated)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
