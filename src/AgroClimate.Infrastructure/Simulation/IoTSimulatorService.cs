using System.Collections.Concurrent;
using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Domain.Services;
using AgroClimate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgroClimate.Infrastructure.Simulation;

public sealed class SimulatorController : ISimulatorController
{
    private readonly object _lock = new();
    private bool _isRunning = true;
    private SimulationScenario _scenario = SimulationScenario.NormalOperation;
    private DateTime _scenarioStartedAtUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<int, bool> _mitigatedZones = new();

    public SimulatorStatusDto GetStatus()
    {
        lock (_lock)
        {
            var (title, desc, ambient) = DescribeScenario(_scenario);
            return new SimulatorStatusDto(
                IsRunning: _isRunning,
                ActiveScenario: _scenario,
                ScenarioTitle: title,
                ScenarioDescription: desc,
                IrkutskAmbientTempC: ambient,
                TickIntervalMs: 2000,
                ScenarioStartedAtUtc: _scenarioStartedAtUtc);
        }
    }

    public SimulatorStatusDto SetScenario(SimulationScenario scenario)
    {
        lock (_lock)
        {
            _scenario = scenario;
            _isRunning = true;
            _scenarioStartedAtUtc = DateTime.UtcNow;
            _mitigatedZones.Clear();
            return GetStatus();
        }
    }

    public SimulatorStatusDto ToggleRunning(bool? run = null)
    {
        lock (_lock)
        {
            _isRunning = run ?? !_isRunning;
            return GetStatus();
        }
    }

    public void NotifyEmergencyMitigationAction(int zoneId, string checklistInstruction)
    {
        string lower = checklistInstruction.ToLowerInvariant();
        if (lower.Contains("теплогенератор") ||
            lower.Contains("резервн") ||
            lower.Contains("вентиляц") ||
            lower.Contains("перезапуск") ||
            lower.Contains("шлюз"))
        {
            _mitigatedZones[zoneId] = true;
        }
    }

    public bool IsZoneMitigated(int zoneId) => _mitigatedZones.TryGetValue(zoneId, out bool m) && m;

    public static (string Title, string Description, double AmbientTempC) DescribeScenario(SimulationScenario scenario) =>
        scenario switch
        {
            SimulationScenario.IrkutskFrostHeatingFailure => (
                "Ночной заморозок в Иркутске (-36°C) и отказ контура обогрева в Теплице №1",
                "Аварийная остановка циркуляционного насоса контура №1 при внешнем морозе -36.4°C. Стремительное падение T° и рост влажности в зоне рассады томата.",
                -36.4),
            SimulationScenario.GrainSiloSelfHeating => (
                "Очаг самосогревания и рост влажности в силосе пшеницы №2",
                "Активация микрофлоры и дыхания зерновой насыпи яровой пшеницы «Ирень»: быстрый разогрев ядра силоса, скачок CO₂ свыше 2200 ppm и влажности.",
                -16.0),
            SimulationScenario.PotatoStorageDeadMansSwitch => (
                "Обрыв связи / зависание контроллера овощехранилища (Dead Man's Switch)",
                "Зависание промышленного контроллера ESP32-POTATO-03 на шине RS-485. Прекращение поступления пакетов телеметрии и срабатывание сторожевого таймера.",
                -18.5),
            _ => (
                "Штатная работа всех объектов ИрГАУ",
                "Все климатические контуры функционируют в автоматическом режиме ПИД-регулирования внутри целевых агрономических коридоров.",
                -14.2)
        };
}

/// <summary>
/// Управляемый фоновый C# генератор физической телеметрии объектов ИрГАУ.
/// Пишет пакеты в тот же высокоскоростной System.Threading.Channels буфер, что и реальные датчики ESP32.
/// </summary>
public sealed class IoTSimulatorWorker : BackgroundService
{
    private sealed class ZonePhysicsState
    {
        public double TempC;
        public double Humidity;
        public double Co2;
    }

    private readonly SimulatorController _controller;
    private readonly ITelemetryChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClimateRealtimeNotifier _notifier;
    private readonly ILogger<IoTSimulatorWorker> _logger;
    private readonly ConcurrentDictionary<int, ZonePhysicsState> _states = new();

    public IoTSimulatorWorker(
        SimulatorController controller,
        ITelemetryChannel channel,
        IServiceScopeFactory scopeFactory,
        IClimateRealtimeNotifier notifier,
        ILogger<IoTSimulatorWorker> logger)
    {
        _controller = controller;
        _channel = channel;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запущен встроенный IoT-симулятор объектов ИрГАУ.");

        // Ждем завершения миграций и начального сидирования БД
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var status = _controller.GetStatus();
                if (!status.IsRunning)
                    continue;

                await StepSimulationAsync(status.ActiveScenario, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка шага IoT-симулятора.");
            }
        }
    }

    private async Task StepSimulationAsync(SimulationScenario scenario, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroClimateDbContext>();

        var zones = await db.ClimateZones
            .AsNoTracking()
            .Include(z => z.AgroProfile)
            .OrderBy(z => z.Id)
            .ToListAsync(ct);

        if (zones.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var rng = Random.Shared;

        foreach (var zone in zones)
        {
            if (zone.AgroProfile is null)
                continue;

            var p = zone.AgroProfile;
            double optTemp = (p.TargetTempMin + p.TargetTempMax) * 0.5;
            double optHum = (p.TargetHumidityMin + p.TargetHumidityMax) * 0.5;
            double optCo2 = (p.TargetCo2Min + p.TargetCo2Max) * 0.5;

            var st = _states.GetOrAdd(zone.Id, _ => new ZonePhysicsState
            {
                TempC = zone.CurrentTempC != 0 ? zone.CurrentTempC : optTemp,
                Humidity = zone.CurrentHumidityPercent != 0 ? zone.CurrentHumidityPercent : optHum,
                Co2 = zone.CurrentCo2Ppm != 0 ? zone.CurrentCo2Ppm : optCo2
            });

            bool mitigated = _controller.IsZoneMitigated(zone.Id);

            // Сценарий 4: Обрыв связи контроллера овощехранилища (Dead Man's Switch)
            if (scenario == SimulationScenario.PotatoStorageDeadMansSwitch &&
                zone.Code == "STOR-POTATO-03" &&
                !mitigated)
            {
                // Контроллер завис — пакеты не отправляются вообще!
                continue;
            }

            // Сценарий 2: Ночной заморозок в Иркутске (-36°C) и отказ обогрева в Теплице №1
            if (scenario == SimulationScenario.IrkutskFrostHeatingFailure &&
                zone.Code == "GH-TOMATO-01" &&
                !mitigated)
            {
                st.TempC = Math.Max(3.2, st.TempC - (0.95 + rng.NextDouble() * 0.35));
                st.Humidity = Math.Min(95.5, st.Humidity + (1.4 + rng.NextDouble() * 0.6));
                st.Co2 = Math.Clamp(st.Co2 + (rng.NextDouble() - 0.45) * 14.0, 420, 950);
            }
            // Сценарий 3: Очаг самосогревания и рост влажности в силосе пшеницы №2
            else if (scenario == SimulationScenario.GrainSiloSelfHeating &&
                     zone.Code == "SILO-WHEAT-02" &&
                     !mitigated)
            {
                st.TempC = Math.Min(37.5, st.TempC + (1.25 + rng.NextDouble() * 0.45));
                st.Humidity = Math.Min(86.5, st.Humidity + (1.8 + rng.NextDouble() * 0.7));
                st.Co2 = Math.Min(2950.0, st.Co2 + (175.0 + rng.NextDouble() * 65.0));
            }
            else
            {
                // Плавный ПИД-возврат к оптимальному зеленому коридору с естественным микрошумом датчиков
                double pullRate = mitigated ? 0.28 : 0.22;
                st.TempC += (optTemp - st.TempC) * pullRate + (rng.NextDouble() - 0.5) * 0.16;
                st.Humidity += (optHum - st.Humidity) * pullRate + (rng.NextDouble() - 0.5) * 0.55;
                st.Co2 += (optCo2 - st.Co2) * pullRate + (rng.NextDouble() - 0.5) * 12.0;
            }

            double roundedTemp = Math.Round(st.TempC, 2);
            double roundedHum = Math.Round(Math.Clamp(st.Humidity, 10.0, 99.5), 1);
            double roundedCo2 = Math.Round(Math.Clamp(st.Co2, 350.0, 5000.0), 0);
            double dewPoint = AgroPhysicsCalculator.CalculateDewPointC(roundedTemp, roundedHum);
            double vpd = AgroPhysicsCalculator.CalculateVpdKpa(roundedTemp, roundedHum);

            var packet = new TelemetryPacketEnvelope(
                ZoneId: zone.Id,
                SensorCode: zone.ControllerNodeId,
                RecordedAtUtc: now,
                TemperatureC: roundedTemp,
                HumidityPercent: roundedHum,
                Co2Ppm: roundedCo2,
                DewPointC: dewPoint,
                VpdKpa: vpd,
                IsSimulated: true);

            await _channel.EnqueueAsync(packet, ct);
        }
    }
}
