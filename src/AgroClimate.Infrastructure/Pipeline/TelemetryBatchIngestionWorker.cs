using System.Data;
using System.Diagnostics;
using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Application.Services;
using AgroClimate.Domain.Enums;
using AgroClimate.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgroClimate.Infrastructure.Pipeline;

/// <summary>
/// Фоновый конвейер потоковой записи телеметрии:
/// System.Threading.Channels -> AlertEvaluationEngine -> SqlBulkCopy (Columnstore) -> SignalR.
/// </summary>
public sealed class TelemetryBatchIngestionWorker : BackgroundService
{
    private readonly ITelemetryChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertEvaluationEngine _alertEngine;
    private readonly IClimateRealtimeNotifier _notifier;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelemetryBatchIngestionWorker> _logger;

    public TelemetryBatchIngestionWorker(
        ITelemetryChannel channel,
        IServiceScopeFactory scopeFactory,
        AlertEvaluationEngine alertEngine,
        IClimateRealtimeNotifier notifier,
        IConfiguration configuration,
        ILogger<TelemetryBatchIngestionWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _alertEngine = alertEngine;
        _notifier = notifier;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запущен воркер пакетной вставки телеметрии (Channels -> SqlBulkCopy).");

        await foreach (var batch in _channel.ReadBatchesAsync(250, TimeSpan.FromMilliseconds(120), stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке пакета телеметрии размером {Count}.", batch.Count);
            }
        }
    }

    private async Task ProcessBatchAsync(IReadOnlyList<TelemetryPacketEnvelope> batch, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgroClimateDbContext>();
        var incidentService = scope.ServiceProvider.GetRequiredService<IIncidentService>();

        var zones = await db.ClimateZones
            .Include(z => z.AgroProfile)
            .ToDictionaryAsync(z => z.Id, ct);

        var activeIncidents = await db.Incidents
            .Where(i => i.Status != IncidentStatus.Resolved)
            .GroupBy(i => i.ZoneId)
            .Select(g => g.OrderByDescending(x => x.OpenedAtUtc).First())
            .ToDictionaryAsync(i => i.ZoneId, ct);

        var table = BuildBulkDataTable();
        var latestNotifications = new Dictionary<int, ZoneLiveTelemetryDto>();

        foreach (var packet in batch)
        {
            if (!zones.TryGetValue(packet.ZoneId, out var zone) || zone.AgroProfile is null)
                continue;

            activeIncidents.TryGetValue(zone.Id, out var activeInc);

            var decision = _alertEngine.ProcessSample(
                zone.Id,
                zone.AgroProfile,
                packet.TemperatureC,
                packet.HumidityPercent,
                packet.Co2Ppm,
                packet.DewPointC,
                packet.VpdKpa,
                packet.RecordedAtUtc,
                activeInc is not null,
                activeInc?.Severity);

            // Добавляем строку для пакетной вставки SqlBulkCopy
            var row = table.NewRow();
            row["ZoneId"] = packet.ZoneId;
            row["SensorCode"] = packet.SensorCode;
            row["RecordedAtUtc"] = packet.RecordedAtUtc;
            row["TemperatureC"] = packet.TemperatureC;
            row["HumidityPercent"] = packet.HumidityPercent;
            row["Co2Ppm"] = packet.Co2Ppm;
            row["DewPointC"] = packet.DewPointC;
            row["VpdKpa"] = packet.VpdKpa;
            row["IsSimulated"] = packet.IsSimulated;
            row["ComplianceState"] = (int)decision.ComplianceState;
            table.Rows.Add(row);

            // Обновляем оперативный срез зоны
            zone.IsSensorOnline = true;
            zone.LastTelemetryUtc = packet.RecordedAtUtc;
            zone.CurrentTempC = packet.TemperatureC;
            zone.CurrentHumidityPercent = packet.HumidityPercent;
            zone.CurrentCo2Ppm = packet.Co2Ppm;
            zone.CurrentDewPointC = packet.DewPointC;
            zone.CurrentVpdKpa = packet.VpdKpa;
            zone.CurrentTempVelocityPerMin = decision.TempVelocityPerMin;
            zone.CurrentComplianceState = decision.ComplianceState;
            zone.StatusNote = decision.DiagnosticSummary;

            // Открываем или эскалируем инцидент при срабатывании правил тревог
            if ((decision.Action == AlertDecisionAction.OpenNewIncident ||
                 decision.Action == AlertDecisionAction.EscalateExistingIncident) &&
                decision.Severity.HasValue &&
                decision.TriggerType.HasValue)
            {
                activeInc = await incidentService.EnsureOrEscalateIncidentAsync(
                    zone,
                    zone.AgroProfile,
                    decision.Severity.Value,
                    decision.TriggerType.Value,
                    decision.DiagnosticSummary,
                    packet.TemperatureC,
                    packet.HumidityPercent,
                    packet.Co2Ppm,
                    packet.DewPointC,
                    packet.VpdKpa,
                    decision.EstimatedMinutesToDamage,
                    ct);

                activeIncidents[zone.Id] = activeInc;
            }
            else if (activeInc is not null && activeInc.EstimatedMinutesToDamage != decision.EstimatedMinutesToDamage)
            {
                // Живое обновление счетчика запаса времени на активном инциденте
                activeInc.EstimatedMinutesToDamage = decision.EstimatedMinutesToDamage;
            }

            latestNotifications[zone.Id] = new ZoneLiveTelemetryDto(
                ZoneId: zone.Id,
                ZoneCode: zone.Code,
                ZoneName: zone.Name,
                CropName: zone.AgroProfile.CropName,
                RecordedAtUtc: packet.RecordedAtUtc,
                TemperatureC: packet.TemperatureC,
                HumidityPercent: packet.HumidityPercent,
                Co2Ppm: packet.Co2Ppm,
                DewPointC: packet.DewPointC,
                VpdKpa: packet.VpdKpa,
                TempVelocityPerMin: decision.TempVelocityPerMin,
                ComplianceState: decision.ComplianceState.ToString(),
                StatusNote: decision.DiagnosticSummary,
                IsSensorOnline: true,
                ActiveIncidentId: activeInc?.Id,
                ActiveIncidentSeverity: activeInc?.Severity.ToString(),
                GraceRemainingSeconds: decision.GraceRemainingSeconds);
        }

        if (table.Rows.Count == 0)
            return;

        // 1. Высокоскоростная пакетная запись в MS SQL Server через SqlBulkCopy
        var connStr = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Строка подключения DefaultConnection не настроена.");

        long totalRowsInDb;
        await using (var sqlConn = new SqlConnection(connStr))
        {
            await sqlConn.OpenAsync(ct);

            using (var bulkCopy = new SqlBulkCopy(sqlConn, SqlBulkCopyOptions.CheckConstraints, null))
            {
                bulkCopy.DestinationTableName = "dbo.TelemetryReadings";
                bulkCopy.BatchSize = table.Rows.Count;
                bulkCopy.BulkCopyTimeout = 30;

                bulkCopy.ColumnMappings.Add("ZoneId", "ZoneId");
                bulkCopy.ColumnMappings.Add("SensorCode", "SensorCode");
                bulkCopy.ColumnMappings.Add("RecordedAtUtc", "RecordedAtUtc");
                bulkCopy.ColumnMappings.Add("TemperatureC", "TemperatureC");
                bulkCopy.ColumnMappings.Add("HumidityPercent", "HumidityPercent");
                bulkCopy.ColumnMappings.Add("Co2Ppm", "Co2Ppm");
                bulkCopy.ColumnMappings.Add("DewPointC", "DewPointC");
                bulkCopy.ColumnMappings.Add("VpdKpa", "VpdKpa");
                bulkCopy.ColumnMappings.Add("IsSimulated", "IsSimulated");
                bulkCopy.ColumnMappings.Add("ComplianceState", "ComplianceState");

                await bulkCopy.WriteToServerAsync(table, ct);
            }

            await using var countCmd = new SqlCommand(
                "SELECT ISNULL(SUM(row_count), 0) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID('dbo.TelemetryReadings') AND index_id IN (0, 1);",
                sqlConn);
            var scalar = await countCmd.ExecuteScalarAsync(ct);
            totalRowsInDb = Convert.ToInt64(scalar ?? 0L);
        }

        // 2. Сохраняем оперативный статус зон в EF Core
        await db.SaveChangesAsync(ct);

        sw.Stop();
        _channel.RecordBatchFlushed(table.Rows.Count, sw.Elapsed.TotalMilliseconds, totalRowsInDb);
        var metrics = _channel.GetMetrics();

        // 3. Мгновенная рассылка свежих срезов в браузеры диспетчеров по SignalR
        foreach (var liveDto in latestNotifications.Values)
        {
            await _notifier.BroadcastZoneTelemetryAsync(liveDto, metrics, ct);
        }
    }

    public static DataTable BuildBulkDataTable()
    {
        var table = new DataTable("TelemetryReadings");
        table.Columns.Add("ZoneId", typeof(int));
        table.Columns.Add("SensorCode", typeof(string));
        table.Columns.Add("RecordedAtUtc", typeof(DateTime));
        table.Columns.Add("TemperatureC", typeof(double));
        table.Columns.Add("HumidityPercent", typeof(double));
        table.Columns.Add("Co2Ppm", typeof(double));
        table.Columns.Add("DewPointC", typeof(double));
        table.Columns.Add("VpdKpa", typeof(double));
        table.Columns.Add("IsSimulated", typeof(bool));
        table.Columns.Add("ComplianceState", typeof(int));
        return table;
    }
}
