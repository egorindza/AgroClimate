using AgroClimate.Application.DTOs;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;

namespace AgroClimate.Application.Interfaces;

public interface ITelemetryChannel
{
    ValueTask EnqueueAsync(TelemetryPacketEnvelope packet, CancellationToken ct = default);
    bool TryEnqueue(TelemetryPacketEnvelope packet);
    IAsyncEnumerable<IReadOnlyList<TelemetryPacketEnvelope>> ReadBatchesAsync(int maxBatchSize, TimeSpan maxWait, CancellationToken ct);
    PipelineMetricsDto GetMetrics();
    void RecordBatchFlushed(int count, double durationMs, long columnstoreTotalRows);
}

public interface ITelemetryAnalyticsService
{
    Task<IReadOnlyList<ZoneSlaSummaryDto>> GetZoneSlaSummariesAsync(int hoursBack = 48, CancellationToken ct = default);
    Task<IReadOnlyList<WindowTrendAnomalyDto>> GetWindowTrendRadarAsync(int windowMinutes = 15, CancellationToken ct = default);
    Task<IReadOnlyList<TimeSeriesPointDto>> GetZoneTimeSeriesAsync(int zoneId, int hoursBack = 24, CancellationToken ct = default);
    Task<IReadOnlyList<AgroProfileTemporalHistoryDto>> GetProfileTemporalHistoryAsync(int? profileId = null, CancellationToken ct = default);
    Task<AgroProfileTemporalHistoryDto?> GetProfileSnapshotAsOfAsync(int profileId, DateTime asOfUtc, CancellationToken ct = default);
    Task<(long TotalRows, double LastAnalyticalQueryMs, bool HasColumnstoreIndex, bool HasTemporalTable)> GetSqlDiagnosticsAsync(CancellationToken ct = default);
}

public interface IIncidentService
{
    Task<IReadOnlyList<Incident>> GetIncidentsAsync(IncidentStatus? statusFilter = null, IncidentSeverity? severityFilter = null, CancellationToken ct = default);
    Task<Incident?> GetIncidentWithDetailsAsync(int incidentId, CancellationToken ct = default);
    Task<Incident?> GetActiveIncidentForZoneAsync(int zoneId, CancellationToken ct = default);
    Task<Incident> EnsureOrEscalateIncidentAsync(
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
        CancellationToken ct = default);
    Task<Incident> AcknowledgeAsync(int incidentId, string actorName, string actorRole, CancellationToken ct = default);
    Task<(Incident Incident, IncidentChecklistItem Item)> ToggleChecklistItemAsync(int incidentId, int checklistItemId, string actorName, string actorRole, CancellationToken ct = default);
    Task<(Incident Incident, IncidentTimelineEvent Event)> AddCommentAsync(int incidentId, string actorName, string actorRole, string comment, CancellationToken ct = default);
    Task<Incident> ResolveAsync(int incidentId, string actorName, string actorRole, string rootCause, string resolutionSummary, CancellationToken ct = default);
}

public interface IAgroProfileService
{
    Task<IReadOnlyList<AgroProfile>> GetAllProfilesAsync(CancellationToken ct = default);
    Task<AgroProfile?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AgroProfile> UpdateProfileAsync(AgroProfileUpdateDto dto, string modifiedBy, CancellationToken ct = default);
}

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyCredential>> GetAllKeysAsync(CancellationToken ct = default);
    Task<ApiKeyCredential?> ValidateAndRecordUsageAsync(string rawApiKey, CancellationToken ct = default);
    Task<ApiKeyCredential> CreateKeyAsync(string deviceLabel, int zoneId, string createdBy, CancellationToken ct = default);
    Task<ApiKeyCredential?> ToggleActiveAsync(int keyId, CancellationToken ct = default);
}

public interface ISimulatorController
{
    SimulatorStatusDto GetStatus();
    SimulatorStatusDto SetScenario(SimulationScenario scenario);
    SimulatorStatusDto ToggleRunning(bool? run = null);
    void NotifyEmergencyMitigationAction(int zoneId, string checklistInstruction);
}

public interface IPostmortemPdfService
{
    Task<(byte[] PdfBytes, string FileName)> GeneratePostmortemPdfAsync(int incidentId, CancellationToken ct = default);
}

public interface IClimateRealtimeNotifier
{
    Task BroadcastZoneTelemetryAsync(ZoneLiveTelemetryDto telemetry, PipelineMetricsDto pipeline, CancellationToken ct = default);
    Task BroadcastIncidentEventAsync(IncidentRealtimeEventDto incidentEvent, CancellationToken ct = default);
    Task BroadcastSimulatorStatusAsync(SimulatorStatusDto status, CancellationToken ct = default);
}
