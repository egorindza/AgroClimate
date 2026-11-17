using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Infrastructure.Persistence;
using AgroClimate.Web.Models;
using AgroClimate.Web.Security;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Web.Controllers;

public sealed class ApiKeysController : Controller
{
    private readonly IApiKeyService _apiKeyService;
    private readonly AgroClimateDbContext _db;
    private readonly IValidator<CreateApiKeyRequest> _validator;

    public ApiKeysController(
        IApiKeyService apiKeyService,
        AgroClimateDbContext db,
        IValidator<CreateApiKeyRequest> validator)
    {
        _apiKeyService = apiKeyService;
        _db = db;
        _validator = validator;
    }

    [HttpGet("/ApiKeys")]
    public async Task<IActionResult> Index(int? keyId = null, CancellationToken ct = default)
    {
        var keys = await _apiKeyService.GetAllKeysAsync(ct);
        var zones = await _db.ClimateZones.AsNoTracking().OrderBy(z => z.Id).ToListAsync(ct);

        var sample = keyId.HasValue
            ? keys.FirstOrDefault(k => k.Id == keyId.Value) ?? keys.FirstOrDefault(k => k.IsActive) ?? keys.FirstOrDefault()
            : keys.FirstOrDefault(k => k.IsActive) ?? keys.FirstOrDefault();

        string baseUrl = $"{Request.Scheme}://{Request.Host}";

        return View(new ApiKeysViewModel
        {
            ApiKeys = keys,
            Zones = zones,
            SelectedSampleKey = sample,
            BaseServerUrl = baseUrl,
            CreatedMessage = TempData["KeyCreated"] as string,
            ValidationErrors = TempData["KeyError"] is string err ? new[] { err } : Array.Empty<string>()
        });
    }

    [HttpPost("/ApiKeys/Create")]
    public async Task<IActionResult> Create([FromForm] CreateApiKeyRequest req, CancellationToken ct = default)
    {
        if (!User.CanManageApiKeys())
        {
            TempData["KeyError"] = "Выпуск и отзыв API-ключей контроллеров доступен Главному агроному и Дежурному инженеру КИПиА.";
            return RedirectToAction(nameof(Index));
        }

        var validation = await _validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            TempData["KeyError"] = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        var created = await _apiKeyService.CreateKeyAsync(
            req.DeviceLabel,
            req.ZoneId,
            $"{User.GetFullName()} ({User.GetRoleTitle()})",
            ct);

        TempData["KeyCreated"] = $"Выпущен новый API-ключ для «{created.DeviceLabel}»: {created.ApiKey}";
        return RedirectToAction(nameof(Index), new { keyId = created.Id });
    }

    [HttpPost("/ApiKeys/Toggle/{id:int}")]
    public async Task<IActionResult> Toggle(int id, CancellationToken ct = default)
    {
        if (!User.CanManageApiKeys())
        {
            return Forbid();
        }

        await _apiKeyService.ToggleActiveAsync(id, ct);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            var keys = await _apiKeyService.GetAllKeysAsync(ct);
            return PartialView("_ApiKeysTable", keys);
        }

        return RedirectToAction(nameof(Index));
    }
}
