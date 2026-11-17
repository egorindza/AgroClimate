using AgroClimate.Domain.Enums;

namespace AgroClimate.Domain.Entities;

/// <summary>
/// Инцидент нарушения микроклимата или отказа телеметрии с полным жизненным циклом War Room.
/// </summary>
public class Incident
{
    public int Id { get; set; }
    public string IncidentNumber { get; set; } = string.Empty; // напр. INC-2026-0042
    public int ZoneId { get; set; }
    public ClimateZone? Zone { get; set; }

    public string Title { get; set; } = string.Empty;
    public string AlertSummary { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Warning;
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public IncidentTriggerType TriggerType { get; set; } = IncidentTriggerType.ThresholdBreach;

    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public string? RootCauseAnalysis { get; set; }
    public string? ResolutionSummary { get; set; }

    // Показатели на момент открытия и экстремумы за время аварии
    public double InitialTempC { get; set; }
    public double InitialHumidityPercent { get; set; }
    public double InitialCo2Ppm { get; set; }
    public double InitialDewPointC { get; set; }
    public double InitialVpdKpa { get; set; }
    public double PeakDeviationValue { get; set; }
    public int EstimatedMinutesToDamage { get; set; } = 30;

    public List<IncidentChecklistItem> ChecklistItems { get; set; } = new();
    public List<IncidentTimelineEvent> TimelineEvents { get; set; } = new();

    public bool IsActive => Status != IncidentStatus.Resolved;

    public double? MttaMinutes => AcknowledgedAtUtc.HasValue
        ? Math.Round(Math.Max(0, (AcknowledgedAtUtc.Value - OpenedAtUtc).TotalMinutes), 1)
        : null;

    public double? MttrMinutes => ResolvedAtUtc.HasValue
        ? Math.Round(Math.Max(0, (ResolvedAtUtc.Value - OpenedAtUtc).TotalMinutes), 1)
        : null;

    public int ChecklistCompletionPercent => ChecklistItems.Count == 0
        ? 0
        : (int)Math.Round(100.0 * ChecklistItems.Count(x => x.IsCompleted) / ChecklistItems.Count);

    public static Incident OpenNew(
        int zoneId,
        string incidentNumber,
        string title,
        string summary,
        IncidentSeverity severity,
        IncidentTriggerType triggerType,
        double tempC,
        double humidityPercent,
        double co2Ppm,
        double dewPointC,
        double vpdKpa,
        int estimatedMinutesToDamage,
        IEnumerable<string> playbookSteps,
        DateTime? nowUtc = null)
    {
        var timestamp = nowUtc ?? DateTime.UtcNow;
        var incident = new Incident
        {
            ZoneId = zoneId,
            IncidentNumber = incidentNumber,
            Title = title,
            AlertSummary = summary,
            Severity = severity,
            Status = IncidentStatus.Open,
            TriggerType = triggerType,
            OpenedAtUtc = timestamp,
            InitialTempC = tempC,
            InitialHumidityPercent = humidityPercent,
            InitialCo2Ppm = co2Ppm,
            InitialDewPointC = dewPointC,
            InitialVpdKpa = vpdKpa,
            PeakDeviationValue = tempC,
            EstimatedMinutesToDamage = Math.Max(1, estimatedMinutesToDamage)
        };

        int order = 1;
        foreach (var step in playbookSteps)
        {
            if (string.IsNullOrWhiteSpace(step)) continue;
            incident.ChecklistItems.Add(new IncidentChecklistItem
            {
                StepOrder = order++,
                Instruction = step.Trim(),
                IsCompleted = false
            });
        }

        incident.TimelineEvents.Add(new IncidentTimelineEvent
        {
            OccurredAtUtc = timestamp,
            EventType = TimelineEventType.IncidentOpened,
            AuthorName = "Автоматика AgroClimate",
            AuthorRole = "Ядро мониторинга",
            Message = $"Открыт инцидент [{severity}]: {summary} (расчетный запас времени: {incident.EstimatedMinutesToDamage} мин)."
        });

        return incident;
    }

    public void Acknowledge(string actorName, string actorRole, DateTime? nowUtc = null)
    {
        if (Status == IncidentStatus.Resolved)
            throw new InvalidOperationException("Невозможно принять в работу уже закрытый инцидент.");

        var timestamp = nowUtc ?? DateTime.UtcNow;
        if (!AcknowledgedAtUtc.HasValue)
        {
            AcknowledgedAtUtc = timestamp;
            AcknowledgedBy = $"{actorName} ({actorRole})";
        }

        if (Status == IncidentStatus.Open)
        {
            Status = IncidentStatus.Acknowledged;
        }

        TimelineEvents.Add(new IncidentTimelineEvent
        {
            OccurredAtUtc = timestamp,
            EventType = TimelineEventType.Acknowledged,
            AuthorName = actorName,
            AuthorRole = actorRole,
            Message = "Инцидент принят в работу дежурной сменой. Запущена проверка по аварийному регламенту."
        });
    }

    public IncidentChecklistItem ToggleChecklistItem(int itemId, string actorName, string actorRole, DateTime? nowUtc = null)
    {
        if (Status == IncidentStatus.Resolved)
            throw new InvalidOperationException("Нельзя изменять чек-лист закрытого инцидента.");

        var item = ChecklistItems.FirstOrDefault(x => x.Id == itemId)
            ?? throw new KeyNotFoundException($"Пункт чек-листа #{itemId} не найден.");

        var timestamp = nowUtc ?? DateTime.UtcNow;
        item.IsCompleted = !item.IsCompleted;
        item.CompletedAtUtc = item.IsCompleted ? timestamp : null;
        item.CompletedBy = item.IsCompleted ? $"{actorName} ({actorRole})" : null;

        if (!AcknowledgedAtUtc.HasValue)
        {
            AcknowledgedAtUtc = timestamp;
            AcknowledgedBy = $"{actorName} ({actorRole})";
        }

        Status = ChecklistItems.Any(x => x.IsCompleted)
            ? IncidentStatus.Mitigating
            : IncidentStatus.Acknowledged;

        TimelineEvents.Add(new IncidentTimelineEvent
        {
            OccurredAtUtc = timestamp,
            EventType = TimelineEventType.ChecklistUpdated,
            AuthorName = actorName,
            AuthorRole = actorRole,
            Message = item.IsCompleted
                ? $"Выполнен шаг регламента №{item.StepOrder}: «{item.Instruction}»"
                : $"Снята отметка выполнения с шага №{item.StepOrder}: «{item.Instruction}»"
        });

        return item;
    }

    public IncidentTimelineEvent AddComment(string actorName, string actorRole, string comment, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("Текст комментария не может быть пустым.", nameof(comment));

        var ev = new IncidentTimelineEvent
        {
            OccurredAtUtc = nowUtc ?? DateTime.UtcNow,
            EventType = TimelineEventType.EngineerComment,
            AuthorName = actorName,
            AuthorRole = actorRole,
            Message = comment.Trim()
        };

        TimelineEvents.Add(ev);
        return ev;
    }

    public bool Escalate(IncidentSeverity newSeverity, string reason, int updatedMinutesToDamage, DateTime? nowUtc = null)
    {
        if (Status == IncidentStatus.Resolved)
            return false;

        EstimatedMinutesToDamage = Math.Max(1, Math.Min(EstimatedMinutesToDamage, updatedMinutesToDamage));

        if (newSeverity <= Severity)
            return false;

        var oldSeverity = Severity;
        Severity = newSeverity;
        AlertSummary = reason;

        TimelineEvents.Add(new IncidentTimelineEvent
        {
            OccurredAtUtc = nowUtc ?? DateTime.UtcNow,
            EventType = TimelineEventType.SeverityEscalated,
            AuthorName = "Автоматика AgroClimate",
            AuthorRole = "Правило эскалации",
            Message = $"Эскалация уровня угрозы {oldSeverity} -> {newSeverity}: {reason} (остаток времени до риска порчи: {EstimatedMinutesToDamage} мин)."
        });

        return true;
    }

    public void Resolve(string actorName, string actorRole, string rootCause, string resolutionSummary, DateTime? nowUtc = null)
    {
        if (Status == IncidentStatus.Resolved)
            throw new InvalidOperationException("Инцидент уже закрыт.");

        var timestamp = nowUtc ?? DateTime.UtcNow;
        if (!AcknowledgedAtUtc.HasValue)
        {
            AcknowledgedAtUtc = timestamp;
            AcknowledgedBy = $"{actorName} ({actorRole})";
        }

        Status = IncidentStatus.Resolved;
        ResolvedAtUtc = timestamp;
        ResolvedBy = $"{actorName} ({actorRole})";
        RootCauseAnalysis = rootCause.Trim();
        ResolutionSummary = resolutionSummary.Trim();

        TimelineEvents.Add(new IncidentTimelineEvent
        {
            OccurredAtUtc = timestamp,
            EventType = TimelineEventType.IncidentResolved,
            AuthorName = actorName,
            AuthorRole = actorRole,
            Message = $"Инцидент закрыт. Причина: {RootCauseAnalysis}. Принятые меры: {ResolutionSummary}."
        });
    }
}

public class IncidentChecklistItem
{
    public int Id { get; set; }
    public int IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public int StepOrder { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedBy { get; set; }
}

public class IncidentTimelineEvent
{
    public long Id { get; set; }
    public int IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public TimelineEventType EventType { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
