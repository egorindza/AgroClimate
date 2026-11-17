using AgroClimate.Domain.Enums;

namespace AgroClimate.Domain.Entities;

/// <summary>
/// Запись временного ряда телеметрии микроклимата.
/// Хранится с индексом NONCLUSTERED COLUMNSTORE INDEX для быстрых аналитических выборок T-SQL.
/// </summary>
public class TelemetryReading
{
    public long Id { get; set; }
    public int ZoneId { get; set; }
    public ClimateZone? Zone { get; set; }

    public string SensorCode { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }

    public double TemperatureC { get; set; }
    public double HumidityPercent { get; set; }
    public double Co2Ppm { get; set; }
    public double DewPointC { get; set; }
    public double VpdKpa { get; set; }

    public bool IsSimulated { get; set; }
    public ComplianceState ComplianceState { get; set; }
}
