using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Infrastructure.Services;

public sealed class AgroProfileService : IAgroProfileService
{
    private readonly AgroClimateDbContext _db;

    public AgroProfileService(AgroClimateDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AgroProfile>> GetAllProfilesAsync(CancellationToken ct = default)
    {
        return await _db.AgroProfiles
            .AsNoTracking()
            .Include(p => p.Zones)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<AgroProfile?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.AgroProfiles
            .AsNoTracking()
            .Include(p => p.Zones)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<AgroProfile> UpdateProfileAsync(AgroProfileUpdateDto dto, string modifiedBy, CancellationToken ct = default)
    {
        var profile = await _db.AgroProfiles
            .Include(p => p.Zones)
            .FirstOrDefaultAsync(p => p.Id == dto.Id, ct)
            ?? throw new KeyNotFoundException($"Агро-профиль #{dto.Id} не найден.");

        profile.CropName = dto.CropName.Trim();
        profile.GrowthStage = dto.GrowthStage.Trim();
        profile.ScientificNotes = dto.ScientificNotes.Trim();

        profile.CritTempMin = dto.CritTempMin;
        profile.WarnTempMin = dto.WarnTempMin;
        profile.TargetTempMin = dto.TargetTempMin;
        profile.TargetTempMax = dto.TargetTempMax;
        profile.WarnTempMax = dto.WarnTempMax;
        profile.CritTempMax = dto.CritTempMax;

        profile.CritHumidityMin = dto.CritHumidityMin;
        profile.WarnHumidityMin = dto.WarnHumidityMin;
        profile.TargetHumidityMin = dto.TargetHumidityMin;
        profile.TargetHumidityMax = dto.TargetHumidityMax;
        profile.WarnHumidityMax = dto.WarnHumidityMax;
        profile.CritHumidityMax = dto.CritHumidityMax;

        profile.TargetCo2Min = dto.TargetCo2Min;
        profile.TargetCo2Max = dto.TargetCo2Max;
        profile.WarnCo2Max = dto.WarnCo2Max;
        profile.CritCo2Max = dto.CritCo2Max;

        profile.CritVpdMin = dto.CritVpdMin;
        profile.WarnVpdMin = dto.WarnVpdMin;
        profile.TargetVpdMin = dto.TargetVpdMin;
        profile.TargetVpdMax = dto.TargetVpdMax;
        profile.WarnVpdMax = dto.WarnVpdMax;
        profile.CritVpdMax = dto.CritVpdMax;

        profile.MinDewPointSpreadC = dto.MinDewPointSpreadC;
        profile.MaxAllowedTempDropRatePerMin = dto.MaxAllowedTempDropRatePerMin;
        profile.MaxAllowedTempRiseRatePerMin = dto.MaxAllowedTempRiseRatePerMin;
        profile.GracePeriodSeconds = dto.GracePeriodSeconds;
        profile.BaseTimeToDamageMinutes = dto.BaseTimeToDamageMinutes;
        profile.EmergencyPlaybookSteps = dto.EmergencyPlaybookSteps.Trim();

        profile.LastModifiedBy = modifiedBy;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        // Сохранение автоматически создаст запись в системной исторической таблице dbo.AgroProfilesHistory (SYSTEM_VERSIONING = ON)
        await _db.SaveChangesAsync(ct);
        return profile;
    }
}
