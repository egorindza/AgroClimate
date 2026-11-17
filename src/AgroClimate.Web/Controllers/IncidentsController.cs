using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Web.Models;
using AgroClimate.Web.Security;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AgroClimate.Web.Controllers;

public sealed class IncidentsController : Controller
{
    private readonly IIncidentService _incidentService;
    private readonly ITelemetryAnalyticsService _analytics;
    private readonly ISimulatorController _simulator;
    private readonly IPostmortemPdfService _pdfService;
    private readonly IValidator<ResolveIncidentRequest> _resolveValidator;
    private readonly IValidator<AddIncidentCommentRequest> _commentValidator;

    public IncidentsController(
        IIncidentService incidentService,
        ITelemetryAnalyticsService analytics,
        ISimulatorController simulator,
        IPostmortemPdfService pdfService,
        IValidator<ResolveIncidentRequest> resolveValidator,
        IValidator<AddIncidentCommentRequest> commentValidator)
    {
        _incidentService = incidentService;
        _analytics = analytics;
        _simulator = simulator;
        _pdfService = pdfService;
        _resolveValidator = resolveValidator;
        _commentValidator = commentValidator;
    }

    [HttpGet("/Incidents")]
    public async Task<IActionResult> Index(string status = "all", string severity = "all", CancellationToken ct = default)
    {
        var vm = await BuildIncidentsIndexAsync(status, severity, ct);
        return View(vm);
    }

    [HttpGet("/Incidents/ListPartial")]
    public async Task<IActionResult> ListPartial(string status = "all", string severity = "all", CancellationToken ct = default)
    {
        var vm = await BuildIncidentsIndexAsync(status, severity, ct);
        return PartialView("_IncidentsTable", vm);
    }

    [HttpGet("/Incidents/WarRoom/{id:int}")]
    public async Task<IActionResult> WarRoom(int id, CancellationToken ct = default)
    {
        var incident = await _incidentService.GetIncidentWithDetailsAsync(id, ct);
        if (incident is null)
            return RedirectToAction(nameof(Index));

        AgroProfileTemporalHistoryDto? snapshot = null;
        if (incident.Zone is not null)
        {
            snapshot = await _analytics.GetProfileSnapshotAsOfAsync(
                incident.Zone.AgroProfileId,
                incident.OpenedAtUtc,
                ct);
        }

        var vm = new WarRoomViewModel
        {
            Incident = incident,
            HistoricalProfileSnapshot = snapshot,
            SimulatorStatus = _simulator.GetStatus()
        };

        return View(vm);
    }

    [HttpPost("/Incidents/Acknowledge/{id:int}")]
    public async Task<IActionResult> Acknowledge(int id, CancellationToken ct = default)
    {
        var incident = await _incidentService.AcknowledgeAsync(
            id,
            User.GetFullName(),
            User.GetRoleTitle(),
            ct);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return PartialView("_WarRoomStatusHeader", incident);
        }

        return RedirectToAction(nameof(WarRoom), new { id });
    }

    [HttpPost("/Incidents/ToggleChecklist")]
    public async Task<IActionResult> ToggleChecklist([FromForm] int incidentId, [FromForm] int itemId, CancellationToken ct = default)
    {
        var (incident, _) = await _incidentService.ToggleChecklistItemAsync(
            incidentId,
            itemId,
            User.GetFullName(),
            User.GetRoleTitle(),
            ct);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return PartialView("_WarRoomChecklist", incident);
        }

        return RedirectToAction(nameof(WarRoom), new { id = incidentId });
    }

    [HttpPost("/Incidents/Comment")]
    public async Task<IActionResult> Comment([FromForm] AddIncidentCommentRequest req, CancellationToken ct = default)
    {
        var validation = await _commentValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            if (Request.Headers.ContainsKey("HX-Request"))
            {
                return BadRequest(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            TempData["WarRoomError"] = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(WarRoom), new { id = req.IncidentId });
        }

        await _incidentService.AddCommentAsync(
            req.IncidentId,
            User.GetFullName(),
            User.GetRoleTitle(),
            req.Comment,
            ct);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Ok();
        }

        return RedirectToAction(nameof(WarRoom), new { id = req.IncidentId });
    }

    [HttpPost("/Incidents/Resolve")]
    public async Task<IActionResult> Resolve([FromForm] ResolveIncidentRequest req, CancellationToken ct = default)
    {
        if (!User.CanResolveIncidents())
        {
            TempData["WarRoomError"] = "Закрытие инцидента доступно только Главному агроному или Дежурному инженеру КИПиА.";
            return RedirectToAction(nameof(WarRoom), new { id = req.IncidentId });
        }

        var validation = await _resolveValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            TempData["WarRoomError"] = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(WarRoom), new { id = req.IncidentId });
        }

        await _incidentService.ResolveAsync(
            req.IncidentId,
            User.GetFullName(),
            User.GetRoleTitle(),
            req.RootCauseAnalysis,
            req.ResolutionSummary,
            ct);

        TempData["WarRoomSuccess"] = "Инцидент официально закрыт. Сформирован акт расследования Postmortem PDF.";
        return RedirectToAction(nameof(WarRoom), new { id = req.IncidentId });
    }

    [HttpGet("/Incidents/PostmortemPdf/{id:int}")]
    public async Task<IActionResult> PostmortemPdf(int id, CancellationToken ct = default)
    {
        var (pdfBytes, fileName) = await _pdfService.GeneratePostmortemPdfAsync(id, ct);
        return File(pdfBytes, "application/pdf", fileName);
    }

    private async Task<IncidentsIndexViewModel> BuildIncidentsIndexAsync(string status, string severity, CancellationToken ct)
    {
        var allIncidents = await _incidentService.GetIncidentsAsync(null, null, ct);

        IEnumerable<Incident> filtered = allIncidents;
        string sNorm = (status ?? "all").ToLowerInvariant();
        if (sNorm == "active")
            filtered = filtered.Where(i => i.Status != IncidentStatus.Resolved);
        else if (sNorm == "resolved")
            filtered = filtered.Where(i => i.Status == IncidentStatus.Resolved);

        string sevNorm = (severity ?? "all").ToLowerInvariant();
        if (Enum.TryParse<IncidentSeverity>(sevNorm, true, out var parsedSeverity))
            filtered = filtered.Where(i => i.Severity == parsedSeverity);

        var mttaValues = allIncidents.Where(i => i.MttaMinutes.HasValue).Select(i => i.MttaMinutes!.Value).ToList();
        var mttrValues = allIncidents.Where(i => i.MttrMinutes.HasValue).Select(i => i.MttrMinutes!.Value).ToList();

        return new IncidentsIndexViewModel
        {
            Incidents = filtered.ToList(),
            StatusFilter = sNorm,
            SeverityFilter = sevNorm,
            ActiveCount = allIncidents.Count(i => i.Status != IncidentStatus.Resolved),
            ResolvedCount = allIncidents.Count(i => i.Status == IncidentStatus.Resolved),
            AvgMttaMinutes = mttaValues.Count > 0 ? Math.Round(mttaValues.Average(), 1) : 0,
            AvgMttrMinutes = mttrValues.Count > 0 ? Math.Round(mttrValues.Average(), 1) : 0
        };
    }
}
