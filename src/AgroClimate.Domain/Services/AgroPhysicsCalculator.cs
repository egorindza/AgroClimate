using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;

namespace AgroClimate.Domain.Services;

public sealed record AgroEvaluationResult(
    ComplianceState ComplianceState,
    IncidentSeverity? SuggestedSeverity,
    IncidentTriggerType? TriggerType,
    string DiagnosticSummary,
    int EstimatedMinutesToDamage);

/// <summary>
/// Чистый доменный калькулятор агрофизики микроклимата:
/// точка росы (формула Магнуса-Тетенса WMO/Alduchov-Eskridge),
/// дефицит упругости водяного пара VPD (формула Тетенса),
/// риск выпадения конденсата и динамический запас времени до повреждения культуры.
/// </summary>
public static class AgroPhysicsCalculator
{
    private const double MagnusA = 17.625;
    private const double MagnusB = 243.04;

    /// <summary>
    /// Вычисляет точку росы (°C) по температуре воздуха (°C) и относительной влажности (%).
    /// Корректно работает в сибирском диапазоне от -45°C до +60°C.
    /// </summary>
    public static double CalculateDewPointC(double temperatureC, double relativeHumidityPercent)
    {
        double clampedRh = Math.Clamp(relativeHumidityPercent, 0.5, 100.0);
        double gamma = (MagnusA * temperatureC / (MagnusB + temperatureC)) + Math.Log(clampedRh / 100.0);
        double dewPoint = (MagnusB * gamma) / (MagnusA - gamma);
        return Math.Round(dewPoint, 2);
    }

    /// <summary>
    /// Давление насыщенного водяного пара SVP (кПа) по уравнению Тетенса.
    /// </summary>
    public static double CalculateSaturationVaporPressureKpa(double temperatureC)
    {
        if (temperatureC >= 0.0)
        {
            return 0.61078 * Math.Exp((17.27 * temperatureC) / (temperatureC + 237.3));
        }

        // Над переохлажденной влагой / льдом при отрицательных температурах
        return 0.61078 * Math.Exp((21.875 * temperatureC) / (temperatureC + 265.5));
    }

    /// <summary>
    /// Дефицит упругости водяного пара VPD (кПа).
    /// </summary>
    public static double CalculateVpdKpa(double temperatureC, double relativeHumidityPercent, double leafTempOffsetC = 0.0)
    {
        double clampedRh = Math.Clamp(relativeHumidityPercent, 0.0, 100.0);
        double svpLeaf = CalculateSaturationVaporPressureKpa(temperatureC + leafTempOffsetC);
        double svpAir = CalculateSaturationVaporPressureKpa(temperatureC);
        double actualVaporPressure = svpAir * (clampedRh / 100.0);

        double vpd = Math.Max(0.0, svpLeaf - actualVaporPressure);
        return Math.Round(vpd, 3);
    }

    /// <summary>
    /// Проверка риска выпадения конденсата на листе / клубнях / своде хранилища.
    /// </summary>
    public static bool IsCondensationRisk(double temperatureC, double dewPointC, double minDewPointSpreadC = 1.8)
    {
        return (temperatureC - dewPointC) <= minDewPointSpreadC;
    }

    /// <summary>
    /// Динамическая оценка времени (мин) до риска необратимой гибели / порчи продукции
    /// с учетом текущего запаса до критического порога и скорости температурного тренда (°C/мин).
    /// </summary>
    public static int EstimateMinutesToDamage(
        double currentTempC,
        double critTempMin,
        double critTempMax,
        double tempVelocityPerMin,
        int baseTimeToDamageMinutes,
        IncidentTriggerType triggerType)
    {
        int safeBase = Math.Max(5, baseTimeToDamageMinutes);

        if (triggerType == IncidentTriggerType.DeadMansSwitch)
        {
            return Math.Max(5, (int)Math.Round(safeBase * 0.65));
        }

        // Уже ниже критического минимума (переохлаждение / заморозок)
        if (currentTempC <= critTempMin)
        {
            double overrun = critTempMin - currentTempC;
            double factor = 1.0 / (1.0 + overrun * 0.35);
            return Math.Clamp((int)Math.Round(safeBase * 0.45 * factor), 2, safeBase);
        }

        // Уже выше критического максимума (тепловой шок / самосогревание насыпи)
        if (currentTempC >= critTempMax)
        {
            double overrun = currentTempC - critTempMax;
            double factor = 1.0 / (1.0 + overrun * 0.25);
            return Math.Clamp((int)Math.Round(safeBase * 0.5 * factor), 3, safeBase);
        }

        // Стремительное падение температуры к критическому минимуму
        if (tempVelocityPerMin < -0.15)
        {
            double distanceToCrit = Math.Max(0.5, currentTempC - critTempMin);
            double minutesToReachCrit = distanceToCrit / Math.Abs(tempVelocityPerMin);
            double thermalBuffer = safeBase * 0.30;
            return Math.Clamp((int)Math.Round(minutesToReachCrit + thermalBuffer), 3, safeBase * 2);
        }

        // Стремительный рост температуры к критическому максимуму
        if (tempVelocityPerMin > 0.15)
        {
            double distanceToCrit = Math.Max(0.5, critTempMax - currentTempC);
            double minutesToReachCrit = distanceToCrit / tempVelocityPerMin;
            double thermalBuffer = safeBase * 0.35;
            return Math.Clamp((int)Math.Round(minutesToReachCrit + thermalBuffer), 4, safeBase * 2);
        }

        return safeBase;
    }

    /// <summary>
    /// Комплексная оценка соответствия показаний агро-профилю культуры, включая раннее обнаружение трендов.
    /// </summary>
    public static AgroEvaluationResult EvaluateCompliance(
        double tempC,
        double humidityPercent,
        double co2Ppm,
        double dewPointC,
        double vpdKpa,
        double tempVelocityPerMin,
        AgroProfile profile)
    {
        var reasons = new List<string>();
        IncidentSeverity? maxSeverity = null;
        IncidentTriggerType? primaryTrigger = null;

        void RegisterIssue(IncidentSeverity severity, IncidentTriggerType trigger, string reason)
        {
            reasons.Add(reason);
            if (!maxSeverity.HasValue || severity > maxSeverity.Value)
            {
                maxSeverity = severity;
                primaryTrigger = trigger;
            }
        }

        // 1. Комбинированный признак самосогревания и дыхания зерновой массы / биомассы
        bool highRespirationCo2 = co2Ppm >= profile.WarnCo2Max;
        bool elevatedTemp = tempC > profile.TargetTempMax;
        if (elevatedTemp && highRespirationCo2 && (humidityPercent >= profile.WarnHumidityMax || tempVelocityPerMin >= profile.MaxAllowedTempRiseRatePerMin * 0.7))
        {
            var sev = (tempC >= profile.CritTempMax || co2Ppm >= profile.CritCo2Max)
                ? IncidentSeverity.Emergency
                : IncidentSeverity.Critical;
            RegisterIssue(
                sev,
                IncidentTriggerType.SelfHeatingRisk,
                $"Очаг самосогревания и интенсивного дыхания: T={tempC:F1}°C, CO₂={co2Ppm:F0} ppm, RH={humidityPercent:F1}%");
        }

        // 2. Проверка критических и предупредительных границ температуры
        if (tempC <= profile.CritTempMin)
        {
            var sev = tempC <= profile.CritTempMin - 3.0 ? IncidentSeverity.Emergency : IncidentSeverity.Critical;
            RegisterIssue(sev, IncidentTriggerType.ThresholdBreach,
                $"Критическое переохлаждение: {tempC:F1}°C (крит. мин. {profile.CritTempMin:F1}°C)");
        }
        else if (tempC >= profile.CritTempMax)
        {
            var sev = tempC >= profile.CritTempMax + 4.0 ? IncidentSeverity.Emergency : IncidentSeverity.Critical;
            RegisterIssue(sev, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Критический перегрев: {tempC:F1}°C (крит. макс. {profile.CritTempMax:F1}°C)");
        }
        else if (tempC <= profile.WarnTempMin)
        {
            RegisterIssue(IncidentSeverity.Warning, IncidentTriggerType.ThresholdBreach,
                $"Пониженная температура: {tempC:F1}°C (порог внимания {profile.WarnTempMin:F1}°C)");
        }
        else if (tempC >= profile.WarnTempMax)
        {
            RegisterIssue(IncidentSeverity.Warning, IncidentTriggerType.ThresholdBreach,
                $"Повышенная температура: {tempC:F1}°C (порог внимания {profile.WarnTempMax:F1}°C)");
        }

        // 3. Раннее обнаружение опасного температурного тренда (не срабатывает при штатном возврате из отклонения к норме)
        if (tempVelocityPerMin <= -profile.MaxAllowedTempDropRatePerMin && tempC <= profile.TargetTempMax)
        {
            var sev = Math.Abs(tempVelocityPerMin) >= profile.MaxAllowedTempDropRatePerMin * 1.8 || tempC < profile.TargetTempMin
                ? IncidentSeverity.Critical
                : IncidentSeverity.Warning;
            RegisterIssue(sev, IncidentTriggerType.RapidTrendAnomaly,
                $"Стремительное падение температуры {tempVelocityPerMin:F2} °C/мин (допустимо не быстрее -{profile.MaxAllowedTempDropRatePerMin:F2} °C/мин)");
        }
        else if (tempVelocityPerMin >= profile.MaxAllowedTempRiseRatePerMin && tempC >= profile.TargetTempMin)
        {
            // Если предыдущее измерение было ниже нормы и температура просто вошла в зеленый коридор снизу — не считаем перегревом, пока T <= середины коридора
            double targetMid = (profile.TargetTempMin + profile.TargetTempMax) * 0.5;
            if (tempC > targetMid)
            {
                var sev = tempVelocityPerMin >= profile.MaxAllowedTempRiseRatePerMin * 1.8 || tempC > profile.TargetTempMax
                    ? IncidentSeverity.Critical
                    : IncidentSeverity.Warning;
                RegisterIssue(sev, primaryTrigger ?? IncidentTriggerType.RapidTrendAnomaly,
                    $"Стремительный рост температуры +{tempVelocityPerMin:F2} °C/мин (допустимо не быстрее +{profile.MaxAllowedTempRiseRatePerMin:F2} °C/мин)");
            }
        }

        // 4. Влажность воздуха
        if (humidityPercent <= profile.CritHumidityMin || humidityPercent >= profile.CritHumidityMax)
        {
            RegisterIssue(IncidentSeverity.Critical, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Критическая влажность RH={humidityPercent:F1}% (норма {profile.TargetHumidityMin:F0}–{profile.TargetHumidityMax:F0}%)");
        }
        else if (humidityPercent <= profile.WarnHumidityMin || humidityPercent >= profile.WarnHumidityMax)
        {
            RegisterIssue(IncidentSeverity.Warning, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Отклонение влажности RH={humidityPercent:F1}% (порог {profile.WarnHumidityMin:F0}–{profile.WarnHumidityMax:F0}%)");
        }

        // 5. Концентрация CO2
        if (co2Ppm >= profile.CritCo2Max)
        {
            RegisterIssue(IncidentSeverity.Critical, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Критический уровень CO₂={co2Ppm:F0} ppm (макс. {profile.CritCo2Max:F0} ppm)");
        }
        else if (co2Ppm >= profile.WarnCo2Max)
        {
            RegisterIssue(IncidentSeverity.Warning, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Повышенный CO₂={co2Ppm:F0} ppm (порог {profile.WarnCo2Max:F0} ppm)");
        }

        // 6. Риск выпадения конденсата (сближение с точкой росы)
        double spread = tempC - dewPointC;
        if (spread <= profile.MinDewPointSpreadC)
        {
            var sev = spread <= 0.6 ? IncidentSeverity.Critical : IncidentSeverity.Warning;
            RegisterIssue(sev, primaryTrigger ?? IncidentTriggerType.CondensationBlightRisk,
                $"Риск выпадения конденсата/фитопатогенов: Δ(T−Tdp)={spread:F1}°C при Tdp={dewPointC:F1}°C");
        }

        // 7. Дефицит упругости пара VPD
        if (vpdKpa <= profile.CritVpdMin || vpdKpa >= profile.CritVpdMax)
        {
            RegisterIssue(IncidentSeverity.Critical, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Критический дисбаланс транспирации VPD={vpdKpa:F2} кПа");
        }
        else if (vpdKpa <= profile.WarnVpdMin || vpdKpa >= profile.WarnVpdMax)
        {
            RegisterIssue(IncidentSeverity.Warning, primaryTrigger ?? IncidentTriggerType.ThresholdBreach,
                $"Отклонение VPD={vpdKpa:F2} кПа (целевой коридор {profile.TargetVpdMin:F2}–{profile.TargetVpdMax:F2} кПа)");
        }

        if (maxSeverity.HasValue && primaryTrigger.HasValue)
        {
            var compliance = maxSeverity.Value >= IncidentSeverity.Critical
                ? ComplianceState.Critical
                : ComplianceState.Warning;

            int eta = EstimateMinutesToDamage(
                tempC,
                profile.CritTempMin,
                profile.CritTempMax,
                tempVelocityPerMin,
                profile.BaseTimeToDamageMinutes,
                primaryTrigger.Value);

            return new AgroEvaluationResult(
                compliance,
                maxSeverity.Value,
                primaryTrigger.Value,
                string.Join("; ", reasons),
                eta);
        }

        // Мягкое отклонение за целевой коридор без пересечения Warn-порогов
        bool outsideTarget =
            tempC < profile.TargetTempMin || tempC > profile.TargetTempMax ||
            humidityPercent < profile.TargetHumidityMin || humidityPercent > profile.TargetHumidityMax ||
            vpdKpa < profile.TargetVpdMin || vpdKpa > profile.TargetVpdMax;

        if (outsideTarget)
        {
            return new AgroEvaluationResult(
                ComplianceState.Warning,
                null,
                null,
                "Незначительный дрейф за целевой оптимум (в пределах буферной зоны)",
                profile.BaseTimeToDamageMinutes);
        }

        return new AgroEvaluationResult(
            ComplianceState.Optimal,
            null,
            null,
            "Микроклимат стабилен, все показатели в целевом зеленом коридоре",
            profile.BaseTimeToDamageMinutes);
    }
}
