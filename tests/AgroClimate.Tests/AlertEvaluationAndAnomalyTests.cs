using AgroClimate.Application.Services;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Domain.Services;

namespace AgroClimate.Tests;

public sealed class AlertEvaluationAndAnomalyTests
{
    private static AgroProfile CreateTomatoProfile(int graceSeconds = 20) => new()
    {
        Id = 1,
        Code = "PROF-TOMATO",
        CropName = "Томат «Сибирский скороспелый F1»",
        GrowthStage = "Рассада",
        CritTempMin = 12.0,
        WarnTempMin = 18.0,
        TargetTempMin = 21.0,
        TargetTempMax = 25.0,
        WarnTempMax = 28.0,
        CritTempMax = 33.0,
        CritHumidityMin = 40.0,
        WarnHumidityMin = 55.0,
        TargetHumidityMin = 62.0,
        TargetHumidityMax = 72.0,
        WarnHumidityMax = 80.0,
        CritHumidityMax = 90.0,
        TargetCo2Min = 650.0,
        TargetCo2Max = 950.0,
        WarnCo2Max = 1300.0,
        CritCo2Max = 1800.0,
        CritVpdMin = 0.30,
        WarnVpdMin = 0.55,
        TargetVpdMin = 0.72,
        TargetVpdMax = 1.15,
        WarnVpdMax = 1.45,
        CritVpdMax = 1.85,
        MinDewPointSpreadC = 2.0,
        MaxAllowedTempDropRatePerMin = 0.45,
        MaxAllowedTempRiseRatePerMin = 0.55,
        GracePeriodSeconds = graceSeconds,
        BaseTimeToDamageMinutes = 35,
        EmergencyPlaybookSteps = "Проверить контур|Запустить ТГ-2"
    };

    [Fact]
    public void ProcessSample_HonorsGracePeriodBeforeOpeningIncident_AndResetsOnRecovery()
    {
        var engine = new AlertEvaluationEngine();
        var profile = CreateTomatoProfile(graceSeconds: 20);
        var t0 = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

        double temp = 16.5; // Ниже WarnTempMin (18.0°C) -> Warning
        double hum = 66.0;
        double dp = AgroPhysicsCalculator.CalculateDewPointC(temp, hum);
        double vpd = AgroPhysicsCalculator.CalculateVpdKpa(temp, hum);

        // t = 0s -> отсчет Grace Period (осталось 20 с)
        var d1 = engine.ProcessSample(1, profile, temp, hum, 750, dp, vpd, t0, false, null);
        Assert.Equal(AlertDecisionAction.GracePeriodCountdown, d1.Action);
        Assert.Equal(20, d1.GraceRemainingSeconds);

        // t = 12s -> все еще внутри Grace Period (осталось 8 с)
        var d2 = engine.ProcessSample(1, profile, temp, hum, 750, dp, vpd, t0.AddSeconds(12), false, null);
        Assert.Equal(AlertDecisionAction.GracePeriodCountdown, d2.Action);
        Assert.Equal(8, d2.GraceRemainingSeconds);

        // t = 21s -> Grace Period истек -> открываем инцидент!
        var d3 = engine.ProcessSample(1, profile, temp, hum, 750, dp, vpd, t0.AddSeconds(21), false, null);
        Assert.Equal(AlertDecisionAction.OpenNewIncident, d3.Action);
        Assert.Equal(IncidentSeverity.Warning, d3.Severity);

        // Возврат в зеленый коридор сбрасывает таймер Grace Period
        double optTemp = 23.0;
        var dOpt = engine.ProcessSample(
            1,
            profile,
            optTemp,
            67.0,
            780,
            AgroPhysicsCalculator.CalculateDewPointC(optTemp, 67.0),
            AgroPhysicsCalculator.CalculateVpdKpa(optTemp, 67.0),
            t0.AddSeconds(60),
            false,
            null);
        Assert.Equal(AlertDecisionAction.None, dOpt.Action);
        Assert.Equal(ComplianceState.Optimal, dOpt.ComplianceState);
    }

    [Fact]
    public void ProcessSample_EmergencyBreachBypassesGracePeriodImmediately()
    {
        var engine = new AlertEvaluationEngine();
        var profile = CreateTomatoProfile(graceSeconds: 60);
        var t0 = new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc);

        // Критический заморозок +7.5°C (на 4.5°C ниже CritTempMin = 12.0°C -> Emergency)
        double temp = 7.5;
        double hum = 88.0;
        var decision = engine.ProcessSample(
            zoneId: 10,
            profile: profile,
            tempC: temp,
            humidityPercent: hum,
            co2Ppm: 720,
            dewPointC: AgroPhysicsCalculator.CalculateDewPointC(temp, hum),
            vpdKpa: AgroPhysicsCalculator.CalculateVpdKpa(temp, hum),
            recordedAtUtc: t0,
            hasActiveIncident: false,
            activeIncidentSeverity: null);

        Assert.Equal(AlertDecisionAction.OpenNewIncident, decision.Action);
        Assert.Equal(IncidentSeverity.Emergency, decision.Severity);
        Assert.Equal(0, decision.GraceRemainingSeconds);
    }

    [Fact]
    public void ProcessSample_DetectsEarlyRapidCoolingTrend_EvenInsideTargetGreenCorridor()
    {
        var engine = new AlertEvaluationEngine();
        var profile = CreateTomatoProfile(graceSeconds: 0);
        var t0 = new DateTime(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc);

        // Начальная точка внутри нормы: +24.4°C
        engine.ProcessSample(
            zoneId: 2,
            profile: profile,
            tempC: 24.4,
            humidityPercent: 66.0,
            co2Ppm: 760,
            dewPointC: AgroPhysicsCalculator.CalculateDewPointC(24.4, 66.0),
            vpdKpa: AgroPhysicsCalculator.CalculateVpdKpa(24.4, 66.0),
            recordedAtUtc: t0,
            hasActiveIncident: false,
            activeIncidentSeverity: null);

        // Через 1 минуту температура упала до +21.8°C (все еще внутри целевого коридора 21..25°C, но скорость -2.6 °C/мин!)
        var d2 = engine.ProcessSample(
            zoneId: 2,
            profile: profile,
            tempC: 21.8,
            humidityPercent: 67.0,
            co2Ppm: 760,
            dewPointC: AgroPhysicsCalculator.CalculateDewPointC(21.8, 67.0),
            vpdKpa: AgroPhysicsCalculator.CalculateVpdKpa(21.8, 67.0),
            recordedAtUtc: t0.AddMinutes(1),
            hasActiveIncident: false,
            activeIncidentSeverity: null);

        Assert.Equal(AlertDecisionAction.OpenNewIncident, d2.Action);
        Assert.Equal(IncidentTriggerType.RapidTrendAnomaly, d2.TriggerType);
        Assert.True(d2.TempVelocityPerMin <= -2.0);
    }

    [Fact]
    public void EvaluateCompliance_DetectsGrainSiloSelfHeatingSignature()
    {
        var wheatProfile = new AgroProfile
        {
            CropName = "Пшеница яровая «Ирень»",
            GrowthStage = "Силосное хранение",
            CritTempMin = -5.0,
            WarnTempMin = 2.0,
            TargetTempMin = 6.5,
            TargetTempMax = 12.5,
            WarnTempMax = 17.5,
            CritTempMax = 24.0,
            CritHumidityMin = 35.0,
            WarnHumidityMin = 46.0,
            TargetHumidityMin = 52.0,
            TargetHumidityMax = 64.0,
            WarnHumidityMax = 70.0,
            CritHumidityMax = 78.0,
            TargetCo2Min = 390.0,
            TargetCo2Max = 620.0,
            WarnCo2Max = 950.0,
            CritCo2Max = 1500.0,
            CritVpdMin = 0.20,
            WarnVpdMin = 0.32,
            TargetVpdMin = 0.40,
            TargetVpdMax = 0.78,
            WarnVpdMax = 0.95,
            CritVpdMax = 1.35,
            MinDewPointSpreadC = 2.5,
            MaxAllowedTempRiseRatePerMin = 0.40,
            BaseTimeToDamageMinutes = 60
        };

        double temp = 26.5;
        double hum = 79.0;
        double co2 = 1850.0;
        var res = AgroPhysicsCalculator.EvaluateCompliance(
            temp,
            hum,
            co2,
            AgroPhysicsCalculator.CalculateDewPointC(temp, hum),
            AgroPhysicsCalculator.CalculateVpdKpa(temp, hum),
            tempVelocityPerMin: 0.85,
            wheatProfile);

        Assert.Equal(ComplianceState.Critical, res.ComplianceState);
        Assert.Equal(IncidentSeverity.Emergency, res.SuggestedSeverity);
        Assert.Equal(IncidentTriggerType.SelfHeatingRisk, res.TriggerType);
    }

    [Fact]
    public void ProcessSample_EscalatesActiveWarningIncidentToCritical()
    {
        var engine = new AlertEvaluationEngine();
        var profile = CreateTomatoProfile(graceSeconds: 10);
        var t0 = new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

        double temp = 11.2; // Ниже CritTempMin (12.0°C) -> Critical
        double hum = 78.0;
        var decision = engine.ProcessSample(
            zoneId: 5,
            profile: profile,
            tempC: temp,
            humidityPercent: hum,
            co2Ppm: 800,
            dewPointC: AgroPhysicsCalculator.CalculateDewPointC(temp, hum),
            vpdKpa: AgroPhysicsCalculator.CalculateVpdKpa(temp, hum),
            recordedAtUtc: t0,
            hasActiveIncident: true,
            activeIncidentSeverity: IncidentSeverity.Warning);

        Assert.Equal(AlertDecisionAction.EscalateExistingIncident, decision.Action);
        Assert.Equal(IncidentSeverity.Critical, decision.Severity);
    }
}
