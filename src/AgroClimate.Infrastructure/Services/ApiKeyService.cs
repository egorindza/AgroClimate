using System.Security.Cryptography;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Infrastructure.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly AgroClimateDbContext _db;

    public ApiKeyService(AgroClimateDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ApiKeyCredential>> GetAllKeysAsync(CancellationToken ct = default)
    {
        return await _db.ApiKeyCredentials
            .AsNoTracking()
            .Include(k => k.Zone)
            .OrderByDescending(k => k.IsActive)
            .ThenByDescending(k => k.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<ApiKeyCredential?> ValidateAndRecordUsageAsync(string rawApiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawApiKey))
            return null;

        string trimmed = rawApiKey.Trim();
        var cred = await _db.ApiKeyCredentials
            .Include(k => k.Zone)
            .FirstOrDefaultAsync(k => k.ApiKey == trimmed && k.IsActive, ct);

        if (cred is null)
            return null;

        cred.LastUsedAtUtc = DateTime.UtcNow;
        cred.TotalPacketsReceived += 1;
        await _db.SaveChangesAsync(ct);

        return cred;
    }

    public async Task<ApiKeyCredential> CreateKeyAsync(string deviceLabel, int zoneId, string createdBy, CancellationToken ct = default)
    {
        var zone = await _db.ClimateZones.FirstOrDefaultAsync(z => z.Id == zoneId, ct)
            ?? throw new KeyNotFoundException($"Климатическая зона #{zoneId} не найдена.");

        string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        string fullKey = $"IRGAU-{zone.Code}-{tokenHex}";
        string prefix = fullKey[..Math.Min(14, fullKey.Length)];

        var cred = new ApiKeyCredential
        {
            DeviceLabel = deviceLabel.Trim(),
            ZoneId = zoneId,
            KeyPrefix = prefix,
            ApiKey = fullKey,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ApiKeyCredentials.Add(cred);
        await _db.SaveChangesAsync(ct);

        cred.Zone = zone;
        return cred;
    }

    public async Task<ApiKeyCredential?> ToggleActiveAsync(int keyId, CancellationToken ct = default)
    {
        var cred = await _db.ApiKeyCredentials
            .Include(k => k.Zone)
            .FirstOrDefaultAsync(k => k.Id == keyId, ct);

        if (cred is null)
            return null;

        cred.IsActive = !cred.IsActive;
        await _db.SaveChangesAsync(ct);
        return cred;
    }
}
