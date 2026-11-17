using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Services;
using AgroClimate.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Web.Controllers;

/// <summary>
/// Промышленный REST API шлюз для приема JSON-телеметрии от реальных микроконтроллеров ESP32 / Arduino / STM32 по API-ключу.
/// </summary>
[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryApiController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ITelemetryChannel _channel;
    private readonly ITelemetryAnalyticsService _analytics;
    private readonly ISimulatorController _simulator;
    private readonly AgroClimateDbContext _db;
    private readonly IValidator<TelemetryIngestRequest> _validator;

    public TelemetryApiController(
        IApiKeyService apiKeyService,
        ITelemetryChannel channel,
        ITelemetryAnalyticsService analytics,
        ISimulatorController simulator,
        AgroClimateDbContext db,
        IValidator<TelemetryIngestRequest> validator)
    {
        _apiKeyService = apiKeyService;
        _channel = channel;
        _analytics = analytics;
        _simulator = simulator;
        _db = db;
        _validator = validator;
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] TelemetryIngestRequest request, CancellationToken ct)
    {
        string? apiKey = Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? Request.Query["apiKey"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized(new
            {
                error = "Отсутствует заголовок X-Api-Key.",
                hint = "Передайте действующий API-ключ контроллера ИрГАУ в HTTP-заголовке X-Api-Key."
            });
        }

        var credential = await _apiKeyService.ValidateAndRecordUsageAsync(apiKey, ct);
        if (credential is null)
        {
            return Unauthorized(new
            {
                error = "Недействительный или отозванный API-ключ.",
                keyPrefix = apiKey[..Math.Min(10, apiKey.Length)]
            });
        }

        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "Ошибка валидации пакета телеметрии.",
                details = validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage })
            });
        }

        var zone = await _db.ClimateZones
            .AsNoTracking()
            .Include(z => z.AgroProfile)
            .FirstOrDefaultAsync(z => z.Id == credential.ZoneId, ct);

        if (zone?.AgroProfile is null)
        {
            return NotFound(new { error = "Целевая климатическая зона не найдена." });
        }

        double temp = Math.Round(request.TemperatureC, 2);
        double hum = Math.Round(request.HumidityPercent, 1);
        double co2 = Math.Round(request.Co2Ppm, 0);
        double dewPoint = AgroPhysicsCalculator.CalculateDewPointC(temp, hum);
        double vpd = AgroPhysicsCalculator.CalculateVpdKpa(temp, hum);
        var timestamp = request.RecordedAtUtc ?? DateTime.UtcNow;

        var eval = AgroPhysicsCalculator.EvaluateCompliance(temp, hum, co2, dewPoint, vpd, 0.0, zone.AgroProfile);

        var packet = new TelemetryPacketEnvelope(
            ZoneId: zone.Id,
            SensorCode: request.SensorCode.Trim(),
            RecordedAtUtc: timestamp,
            TemperatureC: temp,
            HumidityPercent: hum,
            Co2Ppm: co2,
            DewPointC: dewPoint,
            VpdKpa: vpd,
            IsSimulated: false);

        await _channel.EnqueueAsync(packet, ct);

        return Accepted(new
        {
            accepted = true,
            zoneId = zone.Id,
            zoneCode = zone.Code,
            zoneName = zone.Name,
            sensorCode = packet.SensorCode,
            recordedAtUtc = timestamp,
            computedPhysics = new
            {
                dewPointC = dewPoint,
                vpdKpa = vpd,
                condensationRisk = AgroPhysicsCalculator.IsCondensationRisk(temp, dewPoint, zone.AgroProfile.MinDewPointSpreadC)
            },
            compliance = new
            {
                state = eval.ComplianceState.ToString(),
                diagnostic = eval.DiagnosticSummary
            },
            pipeline = _channel.GetMetrics()
        });
    }

    [HttpGet("pipeline")]
    public async Task<IActionResult> PipelineHealth(CancellationToken ct)
    {
        var sqlDiag = await _analytics.GetSqlDiagnosticsAsync(ct);
        return Ok(new
        {
            service = "AgroClimate War Room Telemetry Engine (IrGAU)",
            database = "IrGau_AgroClimateDb",
            sqlServerFeatures = new
            {
                columnstoreIndexActive = sqlDiag.HasColumnstoreIndex,
                temporalTablesActive = sqlDiag.HasTemporalTable,
                totalColumnstoreRows = sqlDiag.TotalRows,
                lastAnalyticalQueryMs = sqlDiag.LastAnalyticalQueryMs
            },
            channelPipeline = _channel.GetMetrics(),
            simulator = _simulator.GetStatus()
        });
    }
}
