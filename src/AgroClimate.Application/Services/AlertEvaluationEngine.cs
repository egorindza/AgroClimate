using System.Collections.Concurrent;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Domain.Services;

namespace AgroClimate.Application.Services;

public enum AlertDecisionAction
{
    None = 0,
    GracePeriodCountdown = 1,
    OpenNewIncident = 2,
    EscalateExistingIncident = 3
}

public sealed record AlertDecision(
    AlertDecisionAction Action,
    ComplianceState ComplianceState,
    IncidentSeverity? Severity,
    IncidentTriggerType? TriggerType,
    string DiagnosticSummary,
    double TempVelocityPerMin,
    int EstimatedMinutesToDamage,
    int? GraceRemainingSeconds);

/// <summary>
/// Потокобезопасный движок оценки правил тревог, расчета мгновенной скорости тренда (°C/мин),
/// контроля допустимого времени выхода за норму (Grace Period) и правил эскалации.
/// </summary>
public sealed class AlertEvaluationEngine
{
    private sealed class ZoneRuntimeWindow
    {
        public readonly LinkedList<(DateTime Utc, double TempC)> RecentSamples = new();
        public DateTime? BreachStartedAtUtc;
        public readonly object SyncRoot = new();
    }

    private readonly ConcurrentDictionary<int, ZoneRuntimeWindow> _zoneWindows = new();

    public AlertDecision ProcessSample(
        int zoneId,
        AgroProfile profile,
        double tempC,
        double humidityPercent,
        double co2Ppm,
        double dewPointC,
        double vpdKpa,
        DateTime recordedAtUtc,
        bool hasActiveIncident,
        IncidentSeverity? activeIncidentSeverity)
    {
        var window = _zoneWindows.GetOrAdd(zoneId, _ => new ZoneRuntimeWindow());

        double velocityPerMin;
        lock (window.SyncRoot)
        {
            window.RecentSamples.AddLast((recordedAtUtc, tempC));

            // Храним скользящее окно последних 90 секунд (или до 15 точек) для оперативной производной dT/dt
            while (window.RecentSamples.Count > 15 ||
                   (window.RecentSamples.Count > 2 &&
                    (recordedAtUtc - window.RecentSamples.First!.Value.Utc).TotalSeconds > 90))
            {
                window.RecentSamples.RemoveFirst();
            }

            velocityPerMin = ComputeVelocityPerMinute(window.RecentSamples);
        }

        var eval = AgroPhysicsCalculator.EvaluateCompliance(
            tempC,
            humidityPercent,
            co2Ppm,
            dewPointC,
            vpdKpa,
            velocityPerMin,
            profile);

        // Если показания в норме (или мягкий дрейф без пересечения Warn-порогов) — сбрасываем таймер Grace Period
        if (!eval.SuggestedSeverity.HasValue || !eval.TriggerType.HasValue)
        {
            lock (window.SyncRoot)
            {
                window.BreachStartedAtUtc = null;
            }

            return new AlertDecision(
                AlertDecisionAction.None,
                eval.ComplianceState,
                null,
                null,
                eval.DiagnosticSummary,
                velocityPerMin,
                eval.EstimatedMinutesToDamage,
                null);
        }

        var severity = eval.SuggestedSeverity.Value;
        var trigger = eval.TriggerType.Value;

        // Если уже открыт активный инцидент по этой зоне — проверяем необходимость эскалации
        if (hasActiveIncident)
        {
            if (activeIncidentSeverity.HasValue && severity > activeIncidentSeverity.Value)
            {
                return new AlertDecision(
                    AlertDecisionAction.EscalateExistingIncident,
                    eval.ComplianceState,
                    severity,
                    trigger,
                    eval.DiagnosticSummary,
                    velocityPerMin,
                    eval.EstimatedMinutesToDamage,
                    0);
            }

            return new AlertDecision(
                AlertDecisionAction.None,
                eval.ComplianceState,
                severity,
                trigger,
                eval.DiagnosticSummary,
                velocityPerMin,
                eval.EstimatedMinutesToDamage,
                null);
        }

        // Новая аномалия при отсутствии открытого инцидента:
        // Emergency-угрозы открывают инцидент немедленно (0 сек), остальные ждут истечения GracePeriodSeconds
        if (severity == IncidentSeverity.Emergency || profile.GracePeriodSeconds <= 0)
        {
            lock (window.SyncRoot)
            {
                window.BreachStartedAtUtc = recordedAtUtc;
            }

            return new AlertDecision(
                AlertDecisionAction.OpenNewIncident,
                eval.ComplianceState,
                severity,
                trigger,
                eval.DiagnosticSummary,
                velocityPerMin,
                eval.EstimatedMinutesToDamage,
                0);
        }

        double elapsedSec;
        lock (window.SyncRoot)
        {
            window.BreachStartedAtUtc ??= recordedAtUtc;
            elapsedSec = Math.Max(0.0, (recordedAtUtc - window.BreachStartedAtUtc.Value).TotalSeconds);
        }

        if (elapsedSec >= profile.GracePeriodSeconds)
        {
            return new AlertDecision(
                AlertDecisionAction.OpenNewIncident,
                eval.ComplianceState,
                severity,
                trigger,
                eval.DiagnosticSummary,
                velocityPerMin,
                eval.EstimatedMinutesToDamage,
                0);
        }

        int remaining = Math.Max(1, (int)Math.Ceiling(profile.GracePeriodSeconds - elapsedSec));
        return new AlertDecision(
            AlertDecisionAction.GracePeriodCountdown,
            eval.ComplianceState,
            severity,
            trigger,
            $"{eval.DiagnosticSummary} (Grace Period: {remaining} с до тревоги)",
            velocityPerMin,
            eval.EstimatedMinutesToDamage,
            remaining);
    }

    public void ResetZoneState(int zoneId)
    {
        _zoneWindows.TryRemove(zoneId, out _);
    }

    private static double ComputeVelocityPerMinute(LinkedList<(DateTime Utc, double TempC)> samples)
    {
        if (samples.Count < 2)
            return 0.0;

        var first = samples.First!.Value;
        var last = samples.Last!.Value;
        double deltaMinutes = (last.Utc - first.Utc).TotalMinutes;

        if (deltaMinutes < 0.02) // менее ~1.2 секунды — недостаточная база для производной
            return 0.0;

        double rate = (last.TempC - first.TempC) / deltaMinutes;
        return Math.Round(rate, 2);
    }
}
