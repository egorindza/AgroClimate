using System.Diagnostics;
using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AgroClimate.Infrastructure.Analytics;

/// <summary>
/// Высокоскоростная аналитика на Dapper поверх MS SQL Server:
/// - NONCLUSTERED COLUMNSTORE INDEX для мгновенного агрегирования временных рядов и SLA;
/// - Оконные функции T-SQL (AVG, STDEV, FIRST_VALUE, LAG по скользящему окну) для раннего выявления трендов;
/// - Запросы SYSTEM_VERSIONING Temporal Tables (FOR SYSTEM_TIME ALL / AS OF) для аудита норм.
/// </summary>
public sealed class DapperTelemetryAnalyticsService : ITelemetryAnalyticsService
{
    private readonly string _connectionString;
    private static double _lastAnalyticalQueryMs = 1.2;

    public DapperTelemetryAnalyticsService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Строка подключения DefaultConnection не найдена.");
    }

    public async Task<IReadOnlyList<ZoneSlaSummaryDto>> GetZoneSlaSummariesAsync(int hoursBack = 48, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Оконная функция LAG взвешивает длительность интервалов между точками для точного расчета % времени в зеленой зоне (SLA),
        // а агрегация по всей истории выполняется через индекс NCCI_TelemetryReadings_Analytics
        const string sql = """
            WITH IntervalTelemetry AS (
                SELECT
                    tr.ZoneId,
                    tr.TemperatureC,
                    tr.HumidityPercent,
                    tr.VpdKpa,
                    tr.Co2Ppm,
                    tr.ComplianceState,
                    ISNULL(
                        NULLIF(
                            DATEDIFF(
                                SECOND,
                                LAG(tr.RecordedAtUtc) OVER (PARTITION BY tr.ZoneId ORDER BY tr.RecordedAtUtc),
                                tr.RecordedAtUtc
                            ),
                            0
                        ),
                        60
                    ) AS RawDurationSec
                FROM dbo.TelemetryReadings tr
                WHERE tr.RecordedAtUtc >= DATEADD(HOUR, -@HoursBack, SYSUTCDATETIME())
            ),
            ClampedIntervals AS (
                SELECT
                    ZoneId,
                    TemperatureC,
                    HumidityPercent,
                    VpdKpa,
                    Co2Ppm,
                    ComplianceState,
                    CASE
                        WHEN RawDurationSec < 1 THEN 2
                        WHEN RawDurationSec > 600 THEN 150
                        ELSE RawDurationSec
                    END AS WeightSec
                FROM IntervalTelemetry
            )
            SELECT
                cz.Id AS ZoneId,
                cz.Code AS ZoneCode,
                cz.Name AS ZoneName,
                ap.CropName AS CropName,
                COUNT(ci.ZoneId) AS TotalSamples,
                SUM(CASE WHEN ci.ComplianceState = 0 THEN 1 ELSE 0 END) AS OptimalSamples,
                SUM(CASE WHEN ci.ComplianceState = 1 THEN 1 ELSE 0 END) AS WarningSamples,
                SUM(CASE WHEN ci.ComplianceState >= 2 THEN 1 ELSE 0 END) AS CriticalSamples,
                ROUND(
                    CASE WHEN SUM(CAST(ci.WeightSec AS FLOAT)) > 0
                         THEN 100.0 * SUM(CASE WHEN ci.ComplianceState = 0 THEN CAST(ci.WeightSec AS FLOAT) ELSE 0.0 END)
                              / SUM(CAST(ci.WeightSec AS FLOAT))
                         ELSE 100.0
                    END, 2
                ) AS SlaCompliancePercent,
                ROUND(ISNULL(AVG(ci.TemperatureC), cz.CurrentTempC), 2) AS AvgTempC,
                ROUND(ISNULL(STDEV(ci.TemperatureC), 0.0), 3) AS TempStdDev,
                ROUND(ISNULL(MIN(ci.TemperatureC), cz.CurrentTempC), 2) AS MinTempC,
                ROUND(ISNULL(MAX(ci.TemperatureC), cz.CurrentTempC), 2) AS MaxTempC,
                ROUND(ISNULL(AVG(ci.HumidityPercent), cz.CurrentHumidityPercent), 1) AS AvgHumidityPercent,
                ROUND(ISNULL(AVG(ci.VpdKpa), cz.CurrentVpdKpa), 2) AS AvgVpdKpa,
                ROUND(ISNULL(AVG(ci.Co2Ppm), cz.CurrentCo2Ppm), 0) AS AvgCo2Ppm
            FROM dbo.ClimateZones cz
            INNER JOIN dbo.AgroProfiles ap ON ap.Id = cz.AgroProfileId
            LEFT JOIN ClampedIntervals ci ON ci.ZoneId = cz.Id
            GROUP BY cz.Id, cz.Code, cz.Name, ap.CropName, cz.CurrentTempC, cz.CurrentHumidityPercent, cz.CurrentVpdKpa, cz.CurrentCo2Ppm
            ORDER BY cz.Id;
            """;

        await using var conn = new SqlConnection(_connectionString);
        var command = new CommandDefinition(sql, new { HoursBack = Math.Clamp(hoursBack, 1, 168) }, cancellationToken: ct);
        var rows = (await conn.QueryAsync<ZoneSlaSummaryDto>(command)).AsList();

        sw.Stop();
        _lastAnalyticalQueryMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        return rows;
    }

    public async Task<IReadOnlyList<WindowTrendAnomalyDto>> GetWindowTrendRadarAsync(int windowMinutes = 15, CancellationToken ct = default)
    {
        // Раннее обнаружение опасных трендов оконными функциями T-SQL:
        // LAG, FIRST_VALUE, AVG, STDEV по скользящему окну последних 20 измерений зоны
        const string sql = """
            WITH SlidingWindow AS (
                SELECT
                    tr.ZoneId,
                    tr.RecordedAtUtc,
                    tr.TemperatureC,
                    tr.HumidityPercent,
                    tr.Co2Ppm,
                    LAG(tr.TemperatureC, 1, tr.TemperatureC) OVER (
                        PARTITION BY tr.ZoneId
                        ORDER BY tr.RecordedAtUtc
                    ) AS PrevTempC,
                    FIRST_VALUE(tr.TemperatureC) OVER (
                        PARTITION BY tr.ZoneId
                        ORDER BY tr.RecordedAtUtc
                        ROWS BETWEEN 19 PRECEDING AND CURRENT ROW
                    ) AS WindowFirstTempC,
                    FIRST_VALUE(tr.RecordedAtUtc) OVER (
                        PARTITION BY tr.ZoneId
                        ORDER BY tr.RecordedAtUtc
                        ROWS BETWEEN 19 PRECEDING AND CURRENT ROW
                    ) AS WindowFirstUtc,
                    AVG(tr.TemperatureC) OVER (
                        PARTITION BY tr.ZoneId
                        ORDER BY tr.RecordedAtUtc
                        ROWS BETWEEN 19 PRECEDING AND CURRENT ROW
                    ) AS MovingAvgTempC,
                    ISNULL(STDEV(tr.TemperatureC) OVER (
                        PARTITION BY tr.ZoneId
                        ORDER BY tr.RecordedAtUtc
                        ROWS BETWEEN 19 PRECEDING AND CURRENT ROW
                    ), 0.0) AS MovingStdDevTempC,
                    ROW_NUMBER() OVER (
                        PARTITION BY tr.ZoneId
                        ORDER BY tr.RecordedAtUtc DESC
                    ) AS RnDesc
                FROM dbo.TelemetryReadings tr
                WHERE tr.RecordedAtUtc >= DATEADD(MINUTE, -@WindowMinutes, SYSUTCDATETIME())
            )
            SELECT
                cz.Id AS ZoneId,
                cz.Code AS ZoneCode,
                cz.Name AS ZoneName,
                ISNULL(sw.RecordedAtUtc, ISNULL(cz.LastTelemetryUtc, SYSUTCDATETIME())) AS LatestRecordedAtUtc,
                ROUND(ISNULL(sw.TemperatureC, cz.CurrentTempC), 2) AS LatestTempC,
                ROUND(ISNULL(sw.PrevTempC, cz.CurrentTempC), 2) AS PrevTempC,
                ROUND(ISNULL(sw.WindowFirstTempC, cz.CurrentTempC), 2) AS WindowFirstTempC,
                ROUND(ISNULL(sw.MovingAvgTempC, cz.CurrentTempC), 2) AS MovingAvgTempC,
                ROUND(ISNULL(sw.MovingStdDevTempC, 0.0), 3) AS MovingStdDevTempC,
                ROUND(ISNULL(sw.TemperatureC - sw.PrevTempC, 0.0), 2) AS StepDeltaC,
                ROUND(
                    CASE
                        WHEN sw.WindowFirstUtc IS NOT NULL AND DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) >= 2
                        THEN (sw.TemperatureC - sw.WindowFirstTempC) / (DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) / 60.0)
                        ELSE cz.CurrentTempVelocityPerMin
                    END, 2
                ) AS WindowVelocityPerMin,
                ROUND(ISNULL(sw.HumidityPercent, cz.CurrentHumidityPercent), 1) AS LatestHumidityPercent,
                ROUND(ISNULL(sw.Co2Ppm, cz.CurrentCo2Ppm), 0) AS LatestCo2Ppm,
                CASE
                    WHEN cz.IsSensorOnline = 0 THEN N'Потеря связи с контроллером (Dead Man''s Switch)'
                    WHEN sw.WindowFirstUtc IS NOT NULL
                         AND DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) >= 2
                         AND ((sw.TemperatureC - sw.WindowFirstTempC) / (DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) / 60.0)) <= -ap.MaxAllowedTempDropRatePerMin
                         THEN N'ОПАСНЫЙ ТРЕНД: Стремительное падение T° (риск отказа обогрева до пересечения 0°C!)'
                    WHEN sw.WindowFirstUtc IS NOT NULL
                         AND DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) >= 2
                         AND ((sw.TemperatureC - sw.WindowFirstTempC) / (DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) / 60.0)) >= ap.MaxAllowedTempRiseRatePerMin
                         AND ISNULL(sw.Co2Ppm, cz.CurrentCo2Ppm) >= ap.WarnCo2Max
                         THEN N'ОПАСНЫЙ ТРЕНД: Самосогревание и скачок дыхания CO₂ в насыпи!'
                    WHEN sw.WindowFirstUtc IS NOT NULL
                         AND DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) >= 2
                         AND ((sw.TemperatureC - sw.WindowFirstTempC) / (DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) / 60.0)) >= ap.MaxAllowedTempRiseRatePerMin
                         THEN N'ВНИМАНИЕ: Стремительный разогрев зоны выше допустимой скорости'
                    WHEN ISNULL(sw.MovingStdDevTempC, 0.0) > 1.2
                         THEN N'Повышенная температурная волатильность (высокое СКО σ)'
                    ELSE N'Стационарный режим (тренд в пределах агронормы)'
                END AS TrendVerdict,
                CAST(
                    CASE
                        WHEN cz.IsSensorOnline = 0 THEN 1
                        WHEN sw.WindowFirstUtc IS NOT NULL
                             AND DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) >= 2
                             AND (
                                 ((sw.TemperatureC - sw.WindowFirstTempC) / (DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) / 60.0)) <= -ap.MaxAllowedTempDropRatePerMin
                                 OR ((sw.TemperatureC - sw.WindowFirstTempC) / (DATEDIFF(SECOND, sw.WindowFirstUtc, sw.RecordedAtUtc) / 60.0)) >= ap.MaxAllowedTempRiseRatePerMin
                             ) THEN 1
                        ELSE 0
                    END AS BIT
                ) AS IsEarlyAnomaly
            FROM dbo.ClimateZones cz
            INNER JOIN dbo.AgroProfiles ap ON ap.Id = cz.AgroProfileId
            LEFT JOIN SlidingWindow sw ON sw.ZoneId = cz.Id AND sw.RnDesc = 1
            ORDER BY cz.Id;
            """;

        await using var conn = new SqlConnection(_connectionString);
        var command = new CommandDefinition(sql, new { WindowMinutes = Math.Clamp(windowMinutes, 2, 180) }, cancellationToken: ct);
        var result = await conn.QueryAsync<WindowTrendAnomalyDto>(command);
        return result.AsList();
    }

    public async Task<IReadOnlyList<TimeSeriesPointDto>> GetZoneTimeSeriesAsync(int zoneId, int hoursBack = 24, CancellationToken ct = default)
    {
        int clampedHours = Math.Clamp(hoursBack, 1, 168);

        // Для окна 1 час отдаем детальные бакеты по 1 минуте (или 10 сек для живого потока), для 6ч — 5 мин, для 24-48ч — 15 мин
        int bucketSeconds = clampedHours switch
        {
            <= 1 => 30,
            <= 6 => 300,
            <= 24 => 900,
            _ => 1800
        };

        const string sql = """
            SELECT
                DATEADD(
                    SECOND,
                    (DATEDIFF_BIG(SECOND, '2026-01-01T00:00:00', tr.RecordedAtUtc) / @BucketSeconds) * @BucketSeconds,
                    CAST('2026-01-01T00:00:00' AS DATETIME2)
                ) AS BucketUtc,
                ROUND(AVG(tr.TemperatureC), 2) AS AvgTempC,
                ROUND(MIN(tr.TemperatureC), 2) AS MinTempC,
                ROUND(MAX(tr.TemperatureC), 2) AS MaxTempC,
                ROUND(AVG(tr.DewPointC), 2) AS AvgDewPointC,
                ROUND(AVG(tr.HumidityPercent), 1) AS AvgHumidityPercent,
                ROUND(AVG(tr.VpdKpa), 3) AS AvgVpdKpa,
                ROUND(AVG(tr.Co2Ppm), 0) AS AvgCo2Ppm
            FROM dbo.TelemetryReadings tr
            WHERE tr.ZoneId = @ZoneId
              AND tr.RecordedAtUtc >= DATEADD(HOUR, -@HoursBack, SYSUTCDATETIME())
            GROUP BY
                DATEADD(
                    SECOND,
                    (DATEDIFF_BIG(SECOND, '2026-01-01T00:00:00', tr.RecordedAtUtc) / @BucketSeconds) * @BucketSeconds,
                    CAST('2026-01-01T00:00:00' AS DATETIME2)
                )
            ORDER BY BucketUtc ASC;
            """;

        await using var conn = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { ZoneId = zoneId, HoursBack = clampedHours, BucketSeconds = bucketSeconds },
            cancellationToken: ct);
        return (await conn.QueryAsync<TimeSeriesPointDto>(command)).AsList();
    }

    public async Task<IReadOnlyList<AgroProfileTemporalHistoryDto>> GetProfileTemporalHistoryAsync(int? profileId = null, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                ap.Id,
                ap.Code,
                ap.CropName,
                ap.GrowthStage,
                ap.TargetTempMin,
                ap.TargetTempMax,
                ap.CritTempMin,
                ap.CritTempMax,
                ap.TargetHumidityMin,
                ap.TargetHumidityMax,
                ap.CritCo2Max,
                ap.TargetVpdMin,
                ap.TargetVpdMax,
                ap.GracePeriodSeconds,
                ap.BaseTimeToDamageMinutes,
                ap.LastModifiedBy,
                ap.ValidFromUtc,
                ap.ValidToUtc
            FROM dbo.AgroProfiles FOR SYSTEM_TIME ALL AS ap
            WHERE (@ProfileId IS NULL OR ap.Id = @ProfileId)
            ORDER BY ap.ValidFromUtc DESC;
            """;

        await using var conn = new SqlConnection(_connectionString);
        var command = new CommandDefinition(sql, new { ProfileId = profileId }, cancellationToken: ct);
        return (await conn.QueryAsync<AgroProfileTemporalHistoryDto>(command)).AsList();
    }

    public async Task<AgroProfileTemporalHistoryDto?> GetProfileSnapshotAsOfAsync(int profileId, DateTime asOfUtc, CancellationToken ct = default)
    {
        const string asOfSql = """
            SELECT TOP (1)
                ap.Id,
                ap.Code,
                ap.CropName,
                ap.GrowthStage,
                ap.TargetTempMin,
                ap.TargetTempMax,
                ap.CritTempMin,
                ap.CritTempMax,
                ap.TargetHumidityMin,
                ap.TargetHumidityMax,
                ap.CritCo2Max,
                ap.TargetVpdMin,
                ap.TargetVpdMax,
                ap.GracePeriodSeconds,
                ap.BaseTimeToDamageMinutes,
                ap.LastModifiedBy,
                ap.ValidFromUtc,
                ap.ValidToUtc
            FROM dbo.AgroProfiles FOR SYSTEM_TIME AS OF @AsOfUtc AS ap
            WHERE ap.Id = @ProfileId;
            """;

        await using var conn = new SqlConnection(_connectionString);
        var snapshot = await conn.QueryFirstOrDefaultAsync<AgroProfileTemporalHistoryDto>(
            new CommandDefinition(asOfSql, new { ProfileId = profileId, AsOfUtc = asOfUtc }, cancellationToken: ct));

        if (snapshot is not null)
            return snapshot;

        // Если исторический учебный инцидент датирован раньше инициализации БД — берем самую раннюю ревизию из FOR SYSTEM_TIME ALL
        const string earliestFallbackSql = """
            SELECT TOP (1)
                ap.Id,
                ap.Code,
                ap.CropName,
                ap.GrowthStage,
                ap.TargetTempMin,
                ap.TargetTempMax,
                ap.CritTempMin,
                ap.CritTempMax,
                ap.TargetHumidityMin,
                ap.TargetHumidityMax,
                ap.CritCo2Max,
                ap.TargetVpdMin,
                ap.TargetVpdMax,
                ap.GracePeriodSeconds,
                ap.BaseTimeToDamageMinutes,
                ap.LastModifiedBy,
                ap.ValidFromUtc,
                ap.ValidToUtc
            FROM dbo.AgroProfiles FOR SYSTEM_TIME ALL AS ap
            WHERE ap.Id = @ProfileId
            ORDER BY ap.ValidFromUtc ASC;
            """;

        return await conn.QueryFirstOrDefaultAsync<AgroProfileTemporalHistoryDto>(
            new CommandDefinition(earliestFallbackSql, new { ProfileId = profileId }, cancellationToken: ct));
    }

    public async Task<(long TotalRows, double LastAnalyticalQueryMs, bool HasColumnstoreIndex, bool HasTemporalTable)> GetSqlDiagnosticsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                ISNULL((SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID('dbo.TelemetryReadings') AND index_id IN (0, 1)), 0) AS TotalRows,
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID('dbo.TelemetryReadings')
                      AND type_desc = 'NONCLUSTERED COLUMNSTORE'
                ) THEN 1 ELSE 0 END AS BIT) AS HasColumnstoreIndex,
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM sys.tables
                    WHERE object_id = OBJECT_ID('dbo.AgroProfiles')
                      AND temporal_type = 2
                ) THEN 1 ELSE 0 END AS BIT) AS HasTemporalTable;
            """;

        await using var conn = new SqlConnection(_connectionString);
        var row = await conn.QueryFirstAsync<(long TotalRows, bool HasColumnstoreIndex, bool HasTemporalTable)>(
            new CommandDefinition(sql, cancellationToken: ct));

        return (row.TotalRows, _lastAnalyticalQueryMs, row.HasColumnstoreIndex, row.HasTemporalTable);
    }
}
