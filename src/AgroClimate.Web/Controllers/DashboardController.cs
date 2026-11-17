using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Infrastructure.Persistence;
using AgroClimate.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Web.Controllers;

public sealed class DashboardController : Controller
{
    private readonly AgroClimateDbContext _db;
    private readonly ISimulatorController _simulator;
    private readonly ITelemetryChannel _channel;
    private readonly ITelemetryAnalyticsService _analytics;
    private readonly IClimateRealtimeNotifier _notifier;

    public DashboardController(
        AgroClimateDbContext db,
        ISimulatorController simulator,
        ITelemetryChannel channel,
        ITelemetryAnalyticsService analytics,
        IClimateRealtimeNotifier notifier)
    {
        _db = db;
        _simulator = simulator;
        _channel = channel;
        _analytics = analytics;
        _notifier = notifier;
    }

    [HttpGet("/")]
    [HttpGet("/Dashboard")]
    public async Task<IActionResult> Index(string filter = "all", CancellationToken ct = default)
    {
        var vm = await BuildDashboardViewModelAsync(filter, ct);
        return View(vm);
    }

    [HttpGet("/Dashboard/ZonesPartial")]
    public async Task<IActionResult> ZonesPartial(string filter = "all", CancellationToken ct = default)
    {
        var vm = await BuildDashboardViewModelAsync(filter, ct);
        return PartialView("_ZoneCardsGrid", vm);
    }

    [HttpPost("/Dashboard/SetScenario")]
    public async Task<IActionResult> SetScenario([FromForm] SimulationScenario scenario, CancellationToken ct = default)
    {
        var status = _simulator.SetScenario(scenario);
        await _notifier.BroadcastSimulatorStatusAsync(status, ct);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return PartialView("_SimulatorDeck", status);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/Dashboard/ToggleSimulator")]
    public async Task<IActionResult> ToggleSimulator(CancellationToken ct = default)
    {
        var status = _simulator.ToggleRunning();
        await _notifier.BroadcastSimulatorStatusAsync(status, ct);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return PartialView("_SimulatorDeck", status);
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<DashboardViewModel> BuildDashboardViewModelAsync(string filter, CancellationToken ct)
    {
        var zones = await _db.ClimateZones
            .AsNoTracking()
            .Include(z => z.AgroProfile)
            .OrderBy(z => z.Id)
            .ToListAsync(ct);

        var activeIncidents = await _db.Incidents
            .AsNoTracking()
            .Include(i => i.Zone)
            .Where(i => i.Status != IncidentStatus.Resolved)
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.OpenedAtUtc)
            .ToListAsync(ct);

        var activeByZone = activeIncidents
            .GroupBy(i => i.ZoneId)
            .ToDictionary(g => g.Key, g => g.First());

        string normalized = (filter ?? "all").Trim().ToLowerInvariant();
        IReadOnlyList<ClimateZone> filteredZones = normalized switch
        {
            "optimal" => zones.Where(z => z.IsSensorOnline && z.CurrentComplianceState == ComplianceState.Optimal).ToList(),
            "warning" => zones.Where(z => z.IsSensorOnline && z.CurrentComplianceState == ComplianceState.Warning).ToList(),
            "critical" => zones.Where(z => !z.IsSensorOnline || z.CurrentComplianceState >= ComplianceState.Critical || activeByZone.ContainsKey(z.Id)).ToList(),
            _ => zones
        };

        var slaRows = await _analytics.GetZoneSlaSummariesAsync(48, ct);
        double globalSla = slaRows.Count > 0
            ? Math.Round(slaRows.Average(x => x.SlaCompliancePercent), 2)
            : 99.0;

        return new DashboardViewModel
        {
            Zones = filteredZones,
            ActiveIncidents = activeIncidents,
            SimulatorStatus = _simulator.GetStatus(),
            PipelineMetrics = _channel.GetMetrics(),
            GlobalSlaPercent = globalSla,
            ActiveFilter = normalized,
            ActiveIncidentByZone = activeByZone
        };
    }
}
