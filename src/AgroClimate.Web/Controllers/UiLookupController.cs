using AgroClimate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Web.Controllers;

public sealed class UiLookupController : Controller
{
    private readonly AgroClimateDbContext _db;

    public UiLookupController(AgroClimateDbContext db)
    {
        _db = db;
    }

    [HttpGet("/UiLookup/SearchIndex")]
    public async Task<IActionResult> SearchIndex(CancellationToken ct = default)
    {
        var items = new List<object>
        {
            new { title = "Обзор и карта объектов ИрГАУ", subtitle = "Живая телеметрия, C# IoT-симулятор и сводные показатели", category = "Раздел", url = "/" },
            new { title = "Incident War Room — Журнал инцидентов", subtitle = "Реагирование дежурной смены, MTTA/MTTR и экспорт Postmortem PDF", category = "Раздел", url = "/Incidents" },
            new { title = "Конструктор агро-профилей культур", subtitle = "Пороговые коридоры, Grace Period и аудит Temporal Tables", category = "Раздел", url = "/Profiles" },
            new { title = "Аналитика стабильности и SLA", subtitle = "Графики Chart.js, Columnstore Index и радар оконных функций T-SQL", category = "Раздел", url = "/Analytics" },
            new { title = "Шлюз ESP32 / Arduino и API-ключи", subtitle = "REST API /api/v1/telemetry/ingest, curl и прошивка C++", category = "Раздел", url = "/ApiKeys" },
            new { title = "Выбор демо-роли специалиста ИрГАУ", subtitle = "Главный агроном, Дежурный инженер КИПиА, Лаборант", category = "Профиль", url = "/Auth/Login" }
        };

        var zones = await _db.ClimateZones
            .AsNoTracking()
            .Include(z => z.AgroProfile)
            .OrderBy(z => z.Id)
            .ToListAsync(ct);

        foreach (var z in zones)
        {
            items.Add(new
            {
                title = z.Name,
                subtitle = $"{z.Code} · {z.AgroProfile?.CropName} · T={z.CurrentTempC:F1}°C, RH={z.CurrentHumidityPercent:F0}%",
                category = "Объект ИрГАУ",
                url = $"/Analytics?zoneId={z.Id}&hours=24"
            });
        }

        var profiles = await _db.AgroProfiles
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        foreach (var p in profiles)
        {
            items.Add(new
            {
                title = p.CropName,
                subtitle = $"{p.GrowthStage} · Норма {p.TargetTempMin:F1}..{p.TargetTempMax:F1}°C",
                category = "Агро-профиль",
                url = $"/Profiles?id={p.Id}"
            });
        }

        var incidents = await _db.Incidents
            .AsNoTracking()
            .Include(i => i.Zone)
            .OrderByDescending(i => i.OpenedAtUtc)
            .Take(12)
            .ToListAsync(ct);

        foreach (var inc in incidents)
        {
            items.Add(new
            {
                title = $"{inc.IncidentNumber} — {inc.Title}",
                subtitle = $"{inc.Zone?.Name} · Статус: {inc.Status}",
                category = "War Room",
                url = $"/Incidents/WarRoom/{inc.Id}"
            });
        }

        return Json(items);
    }
}
