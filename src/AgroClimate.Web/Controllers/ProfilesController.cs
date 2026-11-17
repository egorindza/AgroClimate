using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Web.Models;
using AgroClimate.Web.Security;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AgroClimate.Web.Controllers;

public sealed class ProfilesController : Controller
{
    private readonly IAgroProfileService _profileService;
    private readonly ITelemetryAnalyticsService _analyticsService;
    private readonly IValidator<AgroProfileUpdateDto> _validator;

    public ProfilesController(
        IAgroProfileService profileService,
        ITelemetryAnalyticsService analyticsService,
        IValidator<AgroProfileUpdateDto> validator)
    {
        _profileService = profileService;
        _analyticsService = analyticsService;
        _validator = validator;
    }

    [HttpGet("/Profiles")]
    public async Task<IActionResult> Index(int? id = null, CancellationToken ct = default)
    {
        var profiles = await _profileService.GetAllProfilesAsync(ct);
        var selected = id.HasValue
            ? profiles.FirstOrDefault(p => p.Id == id.Value) ?? profiles.First()
            : profiles.First();

        var history = await _analyticsService.GetProfileTemporalHistoryAsync(selected.Id, ct);

        var vm = new ProfilesViewModel
        {
            Profiles = profiles,
            SelectedProfile = selected,
            EditForm = MapToDto(selected),
            TemporalHistory = history,
            SavedBannerMessage = TempData["ProfileSaved"] as string
        };

        return View(vm);
    }

    [HttpPost("/Profiles/Update")]
    public async Task<IActionResult> Update([FromForm] AgroProfileUpdateDto form, CancellationToken ct = default)
    {
        var profiles = await _profileService.GetAllProfilesAsync(ct);
        var selected = profiles.FirstOrDefault(p => p.Id == form.Id) ?? profiles.First();

        if (!User.CanEditAgroProfiles())
        {
            var histDenied = await _analyticsService.GetProfileTemporalHistoryAsync(selected.Id, ct);
            return View("Index", new ProfilesViewModel
            {
                Profiles = profiles,
                SelectedProfile = selected,
                EditForm = form,
                TemporalHistory = histDenied,
                ValidationErrors = new[] { "Изменение агрономических профилей разрешено только роли «Главный агроном / Научный руководитель»." }
            });
        }

        var validation = await _validator.ValidateAsync(form, ct);
        if (!validation.IsValid)
        {
            var histInvalid = await _analyticsService.GetProfileTemporalHistoryAsync(selected.Id, ct);
            return View("Index", new ProfilesViewModel
            {
                Profiles = profiles,
                SelectedProfile = selected,
                EditForm = form,
                TemporalHistory = histInvalid,
                ValidationErrors = validation.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }

        string actor = $"{User.GetFullName()} ({User.GetRoleTitle()})";
        await _profileService.UpdateProfileAsync(form, actor, ct);

        TempData["ProfileSaved"] = $"Агро-профиль «{form.CropName}» успешно обновлен. Предыдущая ревизия автоматически сохранена в SQL Server Temporal Table (SYSTEM_VERSIONING = ON).";
        return RedirectToAction(nameof(Index), new { id = form.Id });
    }

    [HttpGet("/Profiles/HistoryPartial")]
    public async Task<IActionResult> HistoryPartial(int? profileId = null, CancellationToken ct = default)
    {
        var history = await _analyticsService.GetProfileTemporalHistoryAsync(profileId, ct);
        return PartialView("_TemporalHistoryTable", history);
    }

    private static AgroProfileUpdateDto MapToDto(AgroProfile p) => new()
    {
        Id = p.Id,
        CropName = p.CropName,
        GrowthStage = p.GrowthStage,
        ScientificNotes = p.ScientificNotes,
        CritTempMin = p.CritTempMin,
        WarnTempMin = p.WarnTempMin,
        TargetTempMin = p.TargetTempMin,
        TargetTempMax = p.TargetTempMax,
        WarnTempMax = p.WarnTempMax,
        CritTempMax = p.CritTempMax,
        CritHumidityMin = p.CritHumidityMin,
        WarnHumidityMin = p.WarnHumidityMin,
        TargetHumidityMin = p.TargetHumidityMin,
        TargetHumidityMax = p.TargetHumidityMax,
        WarnHumidityMax = p.WarnHumidityMax,
        CritHumidityMax = p.CritHumidityMax,
        TargetCo2Min = p.TargetCo2Min,
        TargetCo2Max = p.TargetCo2Max,
        WarnCo2Max = p.WarnCo2Max,
        CritCo2Max = p.CritCo2Max,
        CritVpdMin = p.CritVpdMin,
        WarnVpdMin = p.WarnVpdMin,
        TargetVpdMin = p.TargetVpdMin,
        TargetVpdMax = p.TargetVpdMax,
        WarnVpdMax = p.WarnVpdMax,
        CritVpdMax = p.CritVpdMax,
        MinDewPointSpreadC = p.MinDewPointSpreadC,
        MaxAllowedTempDropRatePerMin = p.MaxAllowedTempDropRatePerMin,
        MaxAllowedTempRiseRatePerMin = p.MaxAllowedTempRiseRatePerMin,
        GracePeriodSeconds = p.GracePeriodSeconds,
        BaseTimeToDamageMinutes = p.BaseTimeToDamageMinutes,
        EmergencyPlaybookSteps = p.EmergencyPlaybookSteps
    };
}
