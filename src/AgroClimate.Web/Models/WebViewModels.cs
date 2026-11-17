using AgroClimate.Application.DTOs;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;

namespace AgroClimate.Web.Models;

public sealed class DashboardViewModel
{
    public IReadOnlyList<ClimateZone> Zones { get; init; } = Array.Empty<ClimateZone>();
    public IReadOnlyList<Incident> ActiveIncidents { get; init; } = Array.Empty<Incident>();
    public SimulatorStatusDto SimulatorStatus { get; init; } = null!;
    public PipelineMetricsDto PipelineMetrics { get; init; } = null!;
    public double GlobalSlaPercent { get; init; }
    public string ActiveFilter { get; init; } = "all";
    public Dictionary<int, Incident> ActiveIncidentByZone { get; init; } = new();
}

public sealed class IncidentsIndexViewModel
{
    public IReadOnlyList<Incident> Incidents { get; init; } = Array.Empty<Incident>();
    public string StatusFilter { get; init; } = "all";
    public string SeverityFilter { get; init; } = "all";
    public int ActiveCount { get; init; }
    public int ResolvedCount { get; init; }
    public double AvgMttaMinutes { get; init; }
    public double AvgMttrMinutes { get; init; }
}

public sealed class WarRoomViewModel
{
    public Incident Incident { get; init; } = null!;
    public AgroProfileTemporalHistoryDto? HistoricalProfileSnapshot { get; init; }
    public SimulatorStatusDto SimulatorStatus { get; init; } = null!;
}

public sealed class ProfilesViewModel
{
    public IReadOnlyList<AgroProfile> Profiles { get; init; } = Array.Empty<AgroProfile>();
    public AgroProfile SelectedProfile { get; init; } = null!;
    public AgroProfileUpdateDto EditForm { get; init; } = null!;
    public IReadOnlyList<AgroProfileTemporalHistoryDto> TemporalHistory { get; init; } = Array.Empty<AgroProfileTemporalHistoryDto>();
    public string? SavedBannerMessage { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class AnalyticsViewModel
{
    public IReadOnlyList<ClimateZone> Zones { get; init; } = Array.Empty<ClimateZone>();
    public ClimateZone SelectedZone { get; init; } = null!;
    public int SelectedHours { get; init; } = 24;
    public IReadOnlyList<ZoneSlaSummaryDto> SlaSummaries { get; init; } = Array.Empty<ZoneSlaSummaryDto>();
    public IReadOnlyList<WindowTrendAnomalyDto> WindowRadar { get; init; } = Array.Empty<WindowTrendAnomalyDto>();
    public long TotalColumnstoreRows { get; init; }
    public double LastAnalyticalQueryMs { get; init; }
    public bool HasColumnstoreIndex { get; init; }
    public bool HasTemporalTable { get; init; }
}

public sealed class ApiKeysViewModel
{
    public IReadOnlyList<ApiKeyCredential> ApiKeys { get; init; } = Array.Empty<ApiKeyCredential>();
    public IReadOnlyList<ClimateZone> Zones { get; init; } = Array.Empty<ClimateZone>();
    public ApiKeyCredential? SelectedSampleKey { get; init; }
    public string BaseServerUrl { get; init; } = "http://localhost:5102";
    public string? CreatedMessage { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}
