namespace AgroClimate.Domain.Entities;

/// <summary>
/// Агрономический профиль микроклиматических норм под культуру и фазу вегетации/хранения.
/// Хранится в MS SQL Server как System-Versioned Temporal Table (SYSTEM_VERSIONING = ON)
/// для точного аудита норм, действовавших в момент инцидента.
/// </summary>
public class AgroProfile
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CropName { get; set; } = string.Empty;
    public string GrowthStage { get; set; } = string.Empty;
    public string ScientificNotes { get; set; } = string.Empty;

    // Температурный коридор (°C)
    public double TargetTempMin { get; set; }
    public double TargetTempMax { get; set; }
    public double WarnTempMin { get; set; }
    public double WarnTempMax { get; set; }
    public double CritTempMin { get; set; }
    public double CritTempMax { get; set; }

    // Относительная влажность воздуха (%)
    public double TargetHumidityMin { get; set; }
    public double TargetHumidityMax { get; set; }
    public double WarnHumidityMin { get; set; }
    public double WarnHumidityMax { get; set; }
    public double CritHumidityMin { get; set; }
    public double CritHumidityMax { get; set; }

    // Углекислый газ CO2 (ppm) — фотосинтез или индикатор дыхания/самосогревания зерна
    public double TargetCo2Min { get; set; }
    public double TargetCo2Max { get; set; }
    public double WarnCo2Max { get; set; }
    public double CritCo2Max { get; set; }

    // Дефицит упругости водяного пара VPD (кПа)
    public double TargetVpdMin { get; set; }
    public double TargetVpdMax { get; set; }
    public double WarnVpdMin { get; set; }
    public double WarnVpdMax { get; set; }
    public double CritVpdMin { get; set; }
    public double CritVpdMax { get; set; }

    // Минимальный запас (T - Tdp) до выпадения росы/конденсата (°C)
    public double MinDewPointSpreadC { get; set; } = 1.8;

    // Порог опасной скорости изменения температуры (°C/мин) для раннего предупреждения
    public double MaxAllowedTempDropRatePerMin { get; set; } = 0.45;
    public double MaxAllowedTempRiseRatePerMin { get; set; } = 0.45;

    // Допустимое время выхода за порог до открытия инцидента (сек)
    public int GracePeriodSeconds { get; set; } = 15;

    // Базовый запас времени до риска необратимого повреждения/порчи культуры (мин)
    public int BaseTimeToDamageMinutes { get; set; } = 45;

    // Регламент экстренных действий (разделенные через '|' шаги чек-листа для смены)
    public string EmergencyPlaybookSteps { get; set; } = string.Empty;

    // Метаданные аудита изменений
    public string LastModifiedBy { get; set; } = "Система (ИрГАУ)";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ClimateZone> Zones { get; set; } = new List<ClimateZone>();

    public IReadOnlyList<string> GetPlaybookItems() =>
        string.IsNullOrWhiteSpace(EmergencyPlaybookSteps)
            ? Array.Empty<string>()
            : EmergencyPlaybookSteps
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
