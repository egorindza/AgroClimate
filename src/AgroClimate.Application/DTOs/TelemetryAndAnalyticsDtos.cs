using AgroClimate.Domain.Enums;

namespace AgroClimate.Application.DTOs;

public sealed class TelemetryIngestRequest
{
    public string SensorCode { get; set; } = string.Empty;
    public double TemperatureC { get; set; }
    public double HumidityPercent { get; set; }
    public double Co2Ppm { get; set; } = 420.0;
    public DateTime? RecordedAtUtc { get; set; }
}

public sealed record TelemetryPacketEnvelope(
    int ZoneId,
    string SensorCode,
    DateTime RecordedAtUtc,
    double TemperatureC,
    double HumidityPercent,
    double Co2Ppm,
    double DewPointC,
    double VpdKpa,
    bool IsSimulated);

public sealed record ZoneLiveTelemetryDto(
    int ZoneId,
    string ZoneCode,
    string ZoneName,
    string CropName,
    DateTime RecordedAtUtc,
    double TemperatureC,
    double HumidityPercent,
    double Co2Ppm,
    double DewPointC,
    double VpdKpa,
    double TempVelocityPerMin,
    string ComplianceState,
    string StatusNote,
    bool IsSensorOnline,
    int? ActiveIncidentId,
    string? ActiveIncidentSeverity,
    int? GraceRemainingSeconds);

public sealed record PipelineMetricsDto(
    int QueueDepth,
    long TotalEnqueued,
    long TotalPersistedRows,
    long TotalBatchesFlushed,
    double LastFlushDurationMs,
    int LastBatchSize,
    long ColumnstoreTotalRows);

public sealed class ZoneSlaSummaryDto
{
    public int ZoneId { get; set; }
    public string ZoneCode { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string CropName { get; set; } = string.Empty;
    public int TotalSamples { get; set; }
    public int OptimalSamples { get; set; }
    public int WarningSamples { get; set; }
    public int CriticalSamples { get; set; }
    public double SlaCompliancePercent { get; set; }
    public double AvgTempC { get; set; }
    public double TempStdDev { get; set; }
    public double MinTempC { get; set; }
    public double MaxTempC { get; set; }
    public double AvgHumidityPercent { get; set; }
    public double AvgVpdKpa { get; set; }
    public double AvgCo2Ppm { get; set; }
}

public sealed class WindowTrendAnomalyDto
{
    public int ZoneId { get; set; }
    public string ZoneCode { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public DateTime LatestRecordedAtUtc { get; set; }
    public double LatestTempC { get; set; }
    public double PrevTempC { get; set; }
    public double WindowFirstTempC { get; set; }
    public double MovingAvgTempC { get; set; }
    public double MovingStdDevTempC { get; set; }
    public double StepDeltaC { get; set; }
    public double WindowVelocityPerMin { get; set; }
    public double LatestHumidityPercent { get; set; }
    public double LatestCo2Ppm { get; set; }
    public string TrendVerdict { get; set; } = string.Empty;
    public bool IsEarlyAnomaly { get; set; }
}

public sealed class TimeSeriesPointDto
{
    public DateTime BucketUtc { get; set; }
    public double AvgTempC { get; set; }
    public double MinTempC { get; set; }
    public double MaxTempC { get; set; }
    public double AvgDewPointC { get; set; }
    public double AvgHumidityPercent { get; set; }
    public double AvgVpdKpa { get; set; }
    public double AvgCo2Ppm { get; set; }
}

public sealed record SimulatorStatusDto(
    bool IsRunning,
    SimulationScenario ActiveScenario,
    string ScenarioTitle,
    string ScenarioDescription,
    double IrkutskAmbientTempC,
    int TickIntervalMs,
    DateTime ScenarioStartedAtUtc);
