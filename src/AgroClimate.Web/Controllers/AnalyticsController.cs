using AgroClimate.Application.Interfaces;
using AgroClimate.Infrastructure.Persistence;
using AgroClimate.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Web.Controllers;

public sealed class AnalyticsController : Controller
{
    private readonly AgroClimateDbContext _db;
    private readonly ITelemetryAnalyticsService _analytics;

    public AnalyticsController(AgroClimateDbContext db, ITelemetryAnalyticsService analytics)
    {
        _db = db;
        _analytics = analytics;
    }

    [HttpGet("/Analytics")]
    public async Task<IActionResult> Index(int? zoneId = null, int hours = 24, CancellationToken ct = default)
    {
        var zones = await _db.ClimateZones
            .AsNoTracking()
            .Include(z => z.AgroProfile)
            .OrderBy(z => z.Id)
            .ToListAsync(ct);

        var selectedZone = zoneId.HasValue
            ? zones.FirstOrDefault(z => z.Id == zoneId.Value) ?? zones.First()
            : zones.First();

        int clampedHours = Math.Clamp(hours, 1, 48);
        var slaSummaries = await _analytics.GetZoneSlaSummariesAsync(48, ct);
        var radar = await _analytics.GetWindowTrendRadarAsync(15, ct);
        var sqlDiag = await _analytics.GetSqlDiagnosticsAsync(ct);

        var vm = new AnalyticsViewModel
        {
            Zones = zones,
            SelectedZone = selectedZone,
            SelectedHours = clampedHours,
            SlaSummaries = slaSummaries,
            WindowRadar = radar,
            TotalColumnstoreRows = sqlDiag.TotalRows,
            LastAnalyticalQueryMs = sqlDiag.LastAnalyticalQueryMs,
            HasColumnstoreIndex = sqlDiag.HasColumnstoreIndex,
            HasTemporalTable = sqlDiag.HasTemporalTable
        };

        return View(vm);
    }

    [HttpGet("/Analytics/TimeSeriesData")]
    public async Task<IActionResult> TimeSeriesData(int zoneId, int hours = 24, CancellationToken ct = default)
    {
        var zone = await _db.ClimateZones
            .AsNoTracking()
            .Include(z => z.AgroProfile)
            .FirstOrDefaultAsync(z => z.Id == zoneId, ct);

        if (zone is null)
            return NotFound();

        var points = await _analytics.GetZoneTimeSeriesAsync(zoneId, Math.Clamp(hours, 1, 48), ct);
        var irkutskOffset = TimeSpan.FromHours(8);

        return Json(new
        {
            zoneId = zone.Id,
            zoneName = zone.Name,
            cropName = zone.AgroProfile?.CropName ?? string.Empty,
            targetTempMin = zone.AgroProfile?.TargetTempMin ?? 18,
            targetTempMax = zone.AgroProfile?.TargetTempMax ?? 24,
            critTempMin = zone.AgroProfile?.CritTempMin ?? 10,
            critTempMax = zone.AgroProfile?.CritTempMax ?? 32,
            targetVpdMin = zone.AgroProfile?.TargetVpdMin ?? 0.6,
            targetVpdMax = zone.AgroProfile?.TargetVpdMax ?? 1.2,
            labels = points.Select(p => hours <= 6
                ? p.BucketUtc.Add(irkutskOffset).ToString("HH:mm:ss")
                : p.BucketUtc.Add(irkutskOffset).ToString("dd.MM HH:mm")).ToArray(),
            temps = points.Select(p => p.AvgTempC).ToArray(),
            dewPoints = points.Select(p => p.AvgDewPointC).ToArray(),
            humidities = points.Select(p => p.AvgHumidityPercent).ToArray(),
            vpds = points.Select(p => p.AvgVpdKpa).ToArray(),
            co2s = points.Select(p => p.AvgCo2Ppm).ToArray()
        });
    }

    [HttpGet("/Analytics/WindowRadarPartial")]
    public async Task<IActionResult> WindowRadarPartial(CancellationToken ct = default)
    {
        var radar = await _analytics.GetWindowTrendRadarAsync(15, ct);
        return PartialView("_WindowRadarTable", radar);
    }
}
