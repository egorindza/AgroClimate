using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Application.Services;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Infrastructure.Services;

public sealed class IncidentService : IIncidentService
{
    private readonly AgroClimateDbContext _db;
    private readonly IClimateRealtimeNotifier _notifier;
    private readonly ISimulatorController _simulator;
    private readonly AlertEvaluationEngine _alertEngine;

    public IncidentService(
        AgroClimateDbContext db,
        IClimateRealtimeNotifier notifier,
        ISimulatorController simulator,
        AlertEvaluationEngine alertEngine)
    {
        _db = db;
        _notifier = notifier;
        _simulator = simulator;
        _alertEngine = alertEngine;
    }

    public async Task<IReadOnlyList<Incident>> GetIncidentsAsync(
        IncidentStatus? statusFilter = null,
        IncidentSeverity? severityFilter = null,
        CancellationToken ct = default)
    {
        var query = _db.Incidents
            .AsNoTracking()
            .Include(i => i.Zone)
                .ThenInclude(z => z!.AgroProfile)
            .Include(i => i.ChecklistItems)
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(i => i.Status == statusFilter.Value);
        }

        if (severityFilter.HasValue)
        {
            query = query.Where(i => i.Severity == severityFilter.Value);
        }

        return await query
            .OrderBy(i => i.Status == IncidentStatus.Resolved ? 1 : 0)
            .ThenByDescending(i => i.Severity)
            .ThenByDescending(i => i.OpenedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<Incident?> GetIncidentWithDetailsAsync(int incidentId, CancellationToken ct = default)
    {
        var incident = await _db.Incidents
            .AsNoTracking()
            .Include(i => i.Zone)
                .ThenInclude(z => z!.AgroProfile)
            .Include(i => i.ChecklistItems.OrderBy(c => c.StepOrder))
            .Include(i => i.TimelineEvents.OrderByDescending(e => e.OccurredAtUtc))
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct);

        return incident;
    }

    public async Task<Incident?> GetActiveIncidentForZoneAsync(int zoneId, CancellationToken ct = default)
    {
        return await _db.Incidents
            .Include(i => i.ChecklistItems)
            .Include(i => i.TimelineEvents)
            .Where(i => i.ZoneId == zoneId && i.Status != IncidentStatus.Resolved)
            .OrderByDescending(i => i.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Incident> EnsureOrEscalateIncidentAsync(
        ClimateZone zone,
        AgroProfile profile,
        IncidentSeverity severity,
        IncidentTriggerType triggerType,
        string summary,
        double tempC,
        double humidityPercent,
        double co2Ppm,
        double dewPointC,
        double vpdKpa,
        int estimatedMinutesToDamage,
        CancellationToken ct = default)
    {
        var existing = await _db.Incidents
            .Include(i => i.ChecklistItems)
            .Include(i => i.TimelineEvents)
            .Where(i => i.ZoneId == zone.Id && i.Status != IncidentStatus.Resolved)
            .OrderByDescending(i => i.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            // Обновляем пиковое отклонение
            if (Math.Abs(tempC - profile.TargetTempMin) > Math.Abs(existing.PeakDeviationValue - profile.TargetTempMin))
            {
                existing.PeakDeviationValue = tempC;
            }

            bool escalated = existing.Escalate(severity, summary, estimatedMinutesToDamage, DateTime.UtcNow);
            if (escalated)
            {
                await _db.SaveChangesAsync(ct);
                var lastEv = existing.TimelineEvents.Last();
                await _notifier.BroadcastIncidentEventAsync(MapRealtimeEvent(existing, zone.Name, lastEv), ct);
            }

            return existing;
        }

        int totalCount = await _db.Incidents.CountAsync(ct);
        string incNumber = $"INC-{DateTime.UtcNow:yyyy}-{(totalCount + 1):D4}";
        string title = BuildIncidentTitle(zone.Name, triggerType, severity);

        var playbook = BuildPlaybookForTrigger(profile, triggerType, zone.ControllerNodeId);
        var created = Incident.OpenNew(
            zoneId: zone.Id,
            incidentNumber: incNumber,
            title: title,
            summary: summary,
            severity: severity,
            triggerType: triggerType,
            tempC: tempC,
            humidityPercent: humidityPercent,
            co2Ppm: co2Ppm,
            dewPointC: dewPointC,
            vpdKpa: vpdKpa,
            estimatedMinutesToDamage: estimatedMinutesToDamage,
            playbookSteps: playbook,
            nowUtc: DateTime.UtcNow);

        _db.Incidents.Add(created);
        await _db.SaveChangesAsync(ct);

        var openedEvent = created.TimelineEvents.First();
        await _notifier.BroadcastIncidentEventAsync(MapRealtimeEvent(created, zone.Name, openedEvent), ct);

        return created;
    }

    public async Task<Incident> AcknowledgeAsync(int incidentId, string actorName, string actorRole, CancellationToken ct = default)
    {
        var incident = await LoadTrackedIncidentAsync(incidentId, ct);
        incident.Acknowledge(actorName, actorRole, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);

        var ev = incident.TimelineEvents.Last();
        await _notifier.BroadcastIncidentEventAsync(MapRealtimeEvent(incident, incident.Zone?.Name ?? string.Empty, ev), ct);
        return incident;
    }

    public async Task<(Incident Incident, IncidentChecklistItem Item)> ToggleChecklistItemAsync(
        int incidentId,
        int checklistItemId,
        string actorName,
        string actorRole,
        CancellationToken ct = default)
    {
        var incident = await LoadTrackedIncidentAsync(incidentId, ct);
        var item = incident.ToggleChecklistItem(checklistItemId, actorName, actorRole, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);

        if (item.IsCompleted)
        {
            _simulator.NotifyEmergencyMitigationAction(incident.ZoneId, item.Instruction);
        }

        var ev = incident.TimelineEvents.Last();
        await _notifier.BroadcastIncidentEventAsync(
            MapRealtimeEvent(
                incident,
                incident.Zone?.Name ?? string.Empty,
                ev,
                item.Id,
                item.IsCompleted,
                item.CompletedBy),
            ct);

        return (incident, item);
    }

    public async Task<(Incident Incident, IncidentTimelineEvent Event)> AddCommentAsync(
        int incidentId,
        string actorName,
        string actorRole,
        string comment,
        CancellationToken ct = default)
    {
        var incident = await LoadTrackedIncidentAsync(incidentId, ct);
        var ev = incident.AddComment(actorName, actorRole, comment, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);

        await _notifier.BroadcastIncidentEventAsync(MapRealtimeEvent(incident, incident.Zone?.Name ?? string.Empty, ev), ct);
        return (incident, ev);
    }

    public async Task<Incident> ResolveAsync(
        int incidentId,
        string actorName,
        string actorRole,
        string rootCause,
        string resolutionSummary,
        CancellationToken ct = default)
    {
        var incident = await LoadTrackedIncidentAsync(incidentId, ct);
        incident.Resolve(actorName, actorRole, rootCause, resolutionSummary, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);

        _alertEngine.ResetZoneState(incident.ZoneId);

        var ev = incident.TimelineEvents.Last();
        await _notifier.BroadcastIncidentEventAsync(MapRealtimeEvent(incident, incident.Zone?.Name ?? string.Empty, ev), ct);
        return incident;
    }

    private async Task<Incident> LoadTrackedIncidentAsync(int incidentId, CancellationToken ct)
    {
        return await _db.Incidents
            .Include(i => i.Zone)
                .ThenInclude(z => z!.AgroProfile)
            .Include(i => i.ChecklistItems.OrderBy(c => c.StepOrder))
            .Include(i => i.TimelineEvents)
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new KeyNotFoundException($"Инцидент #{incidentId} не найден.");
    }

    private static string BuildIncidentTitle(string zoneName, IncidentTriggerType triggerType, IncidentSeverity severity)
    {
        return triggerType switch
        {
            IncidentTriggerType.DeadMansSwitch => $"Обрыв телеметрии контроллера (Dead Man's Switch) — {zoneName}",
            IncidentTriggerType.SelfHeatingRisk => $"Очаг самосогревания и рост дыхания CO₂ — {zoneName}",
            IncidentTriggerType.RapidTrendAnomaly => $"Опасный скоростной тренд температуры — {zoneName}",
            IncidentTriggerType.CondensationBlightRisk => $"Угроза выпадения конденсата (точка росы) — {zoneName}",
            _ => severity >= IncidentSeverity.Critical
                ? $"Критический выход микроклимата за пороги — {zoneName}"
                : $"Отклонение микроклимата от агронормы — {zoneName}"
        };
    }

    private static IReadOnlyList<string> BuildPlaybookForTrigger(AgroProfile profile, IncidentTriggerType triggerType, string controllerId)
    {
        if (triggerType == IncidentTriggerType.DeadMansSwitch)
        {
            var dmsSteps = new List<string>
            {
                $"Проверить питание 24В и линию RS-485 / Wi-Fi шлюза контроллера {controllerId}",
                "Выполнить аппаратный перезапуск сторожевого таймера (Watchdog Reset) на шкафу автоматики",
                "Произвести контрольный ручной замер температуры и влажности переносным термогигрометром Testo",
                "При отказе основного узла переключить линию опроса на резервный шлюз ESP32-BACKUP"
            };
            return dmsSteps;
        }

        var items = profile.GetPlaybookItems();
        if (items.Count > 0)
            return items;

        return new[]
        {
            "Проверить исполнительные механизмы отопления, вентиляции и увлажнения зоны",
            "Запустить резервный контур поддержания микроклимата",
            "Уведомить главного агронома и зафиксировать контрольные показания"
        };
    }

    private static IncidentRealtimeEventDto MapRealtimeEvent(
        Incident incident,
        string zoneName,
        IncidentTimelineEvent ev,
        int? toggledItemId = null,
        bool? toggledCompleted = null,
        string? toggledBy = null)
    {
        return new IncidentRealtimeEventDto(
            IncidentId: incident.Id,
            IncidentNumber: incident.IncidentNumber,
            ZoneId: incident.ZoneId,
            ZoneName: zoneName,
            Title: incident.Title,
            Severity: incident.Severity.ToString(),
            Status: incident.Status.ToString(),
            EstimatedMinutesToDamage: incident.EstimatedMinutesToDamage,
            ChecklistCompletionPercent: incident.ChecklistCompletionPercent,
            EventType: ev.EventType.ToString(),
            AuthorName: ev.AuthorName,
            AuthorRole: ev.AuthorRole,
            Message: ev.Message,
            OccurredAtUtc: ev.OccurredAtUtc,
            ToggledChecklistItemId: toggledItemId,
            ToggledChecklistCompleted: toggledCompleted,
            ToggledChecklistBy: toggledBy);
    }
}
