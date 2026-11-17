using AgroClimate.Domain.Enums;

namespace AgroClimate.Application.DTOs;

public sealed class AgroProfileUpdateDto
{
    public int Id { get; set; }
    public string CropName { get; set; } = string.Empty;
    public string GrowthStage { get; set; } = string.Empty;
    public string ScientificNotes { get; set; } = string.Empty;

    public double CritTempMin { get; set; }
    public double WarnTempMin { get; set; }
    public double TargetTempMin { get; set; }
    public double TargetTempMax { get; set; }
    public double WarnTempMax { get; set; }
    public double CritTempMax { get; set; }

    public double CritHumidityMin { get; set; }
    public double WarnHumidityMin { get; set; }
    public double TargetHumidityMin { get; set; }
    public double TargetHumidityMax { get; set; }
    public double WarnHumidityMax { get; set; }
    public double CritHumidityMax { get; set; }

    public double TargetCo2Min { get; set; }
    public double TargetCo2Max { get; set; }
    public double WarnCo2Max { get; set; }
    public double CritCo2Max { get; set; }

    public double CritVpdMin { get; set; }
    public double WarnVpdMin { get; set; }
    public double TargetVpdMin { get; set; }
    public double TargetVpdMax { get; set; }
    public double WarnVpdMax { get; set; }
    public double CritVpdMax { get; set; }

    public double MinDewPointSpreadC { get; set; }
    public double MaxAllowedTempDropRatePerMin { get; set; }
    public double MaxAllowedTempRiseRatePerMin { get; set; }

    public int GracePeriodSeconds { get; set; }
    public int BaseTimeToDamageMinutes { get; set; }
    public string EmergencyPlaybookSteps { get; set; } = string.Empty;
}

public sealed class AgroProfileTemporalHistoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CropName { get; set; } = string.Empty;
    public string GrowthStage { get; set; } = string.Empty;
    public double TargetTempMin { get; set; }
    public double TargetTempMax { get; set; }
    public double CritTempMin { get; set; }
    public double CritTempMax { get; set; }
    public double TargetHumidityMin { get; set; }
    public double TargetHumidityMax { get; set; }
    public double CritCo2Max { get; set; }
    public double TargetVpdMin { get; set; }
    public double TargetVpdMax { get; set; }
    public int GracePeriodSeconds { get; set; }
    public int BaseTimeToDamageMinutes { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;
    public DateTime ValidFromUtc { get; set; }
    public DateTime ValidToUtc { get; set; }
    public bool IsCurrent => ValidToUtc.Year >= 9999;
}

public sealed class ResolveIncidentRequest
{
    public int IncidentId { get; set; }
    public string RootCauseAnalysis { get; set; } = string.Empty;
    public string ResolutionSummary { get; set; } = string.Empty;
}

public sealed class AddIncidentCommentRequest
{
    public int IncidentId { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed class CreateApiKeyRequest
{
    public string DeviceLabel { get; set; } = string.Empty;
    public int ZoneId { get; set; }
}

public sealed record IncidentRealtimeEventDto(
    int IncidentId,
    string IncidentNumber,
    int ZoneId,
    string ZoneName,
    string Title,
    string Severity,
    string Status,
    int EstimatedMinutesToDamage,
    int ChecklistCompletionPercent,
    string EventType,
    string AuthorName,
    string AuthorRole,
    string Message,
    DateTime OccurredAtUtc,
    int? ToggledChecklistItemId = null,
    bool? ToggledChecklistCompleted = null,
    string? ToggledChecklistBy = null);
