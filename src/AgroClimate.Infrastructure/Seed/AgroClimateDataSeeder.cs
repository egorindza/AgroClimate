using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Domain.Services;
using AgroClimate.Infrastructure.Persistence;
using AgroClimate.Infrastructure.Pipeline;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgroClimate.Infrastructure.Seed;

public sealed class AgroClimateDataSeeder
{
    private readonly AgroClimateDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ITelemetryChannel _channel;
    private readonly ILogger<AgroClimateDataSeeder> _logger;

    public AgroClimateDataSeeder(
        AgroClimateDbContext db,
        IConfiguration configuration,
        ITelemetryChannel channel,
        ILogger<AgroClimateDataSeeder> logger)
    {
        _db = db;
        _configuration = configuration;
        _channel = channel;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _db.Database.MigrateAsync(ct);

        if (await _db.ClimateZones.AnyAsync(ct))
        {
            var restartNow = DateTime.UtcNow;
            var existingZones = await _db.ClimateZones.ToListAsync(ct);
            foreach (var z in existingZones)
            {
                z.LastTelemetryUtc = restartNow;
                z.IsSensorOnline = true;
            }
            await _db.SaveChangesAsync(ct);

            long existingRows = await _db.TelemetryReadings.LongCountAsync(ct);
            _channel.RecordBatchFlushed(0, 0, existingRows);
            _logger.LogInformation("БД IrGau_AgroClimateDb уже проинициализирована ({Rows} строк телеметрии).", existingRows);
            return;
        }

        _logger.LogInformation("Запуск первичного наполнения БД IrGau_AgroClimateDb демонстрационными данными ИрГАУ...");
        var now = DateTime.UtcNow;

        // 1. Агро-профили сибирских культур и биолабораторий ИрГАУ
        var pTomato = new AgroProfile
        {
            Code = "PROF-TOMATO-SEEDLING",
            CropName = "Томат «Сибирский скороспелый F1»",
            GrowthStage = "Рассада (фаза 4–5 настоящих листьев)",
            ScientificNotes = "Требует стабильного корневого и воздушного тепла при резких ночных морозах Прибайкалья. Недопустим конденсат на листовой пластинке.",
            CritTempMin = 12.0,
            WarnTempMin = 18.0,
            TargetTempMin = 21.0,
            TargetTempMax = 25.0,
            WarnTempMax = 28.0,
            CritTempMax = 33.0,
            CritHumidityMin = 40.0,
            WarnHumidityMin = 55.0,
            TargetHumidityMin = 62.0,
            TargetHumidityMax = 72.0,
            WarnHumidityMax = 80.0,
            CritHumidityMax = 90.0,
            TargetCo2Min = 650.0,
            TargetCo2Max = 950.0,
            WarnCo2Max = 1300.0,
            CritCo2Max = 1800.0,
            CritVpdMin = 0.30,
            WarnVpdMin = 0.55,
            TargetVpdMin = 0.72,
            TargetVpdMax = 1.15,
            WarnVpdMax = 1.45,
            CritVpdMax = 1.85,
            MinDewPointSpreadC = 2.0,
            MaxAllowedTempDropRatePerMin = 0.45,
            MaxAllowedTempRiseRatePerMin = 0.60,
            GracePeriodSeconds = 6,
            BaseTimeToDamageMinutes = 35,
            EmergencyPlaybookSteps =
                "Проверить давление и температуру прямой/обратной воды в контуре обогрева №1|" +
                "Запустить резервный дизельный теплогенератор ТГ-2 и открыть байпасный клапан|" +
                "Опустить энергосберегающий горизонтальный термоэкран теплицы|" +
                "Контролировать дефицит VPD и исключить выпадение росы на верхушечной почке",
            LastModifiedBy = "Кафедра растениеводства ИрГАУ (базовый регламент)",
            UpdatedAtUtc = now.AddDays(-10)
        };

        var pMicrogreens = new AgroProfile
        {
            Code = "PROF-MICROGREENS",
            CropName = "Микрозелень (редис «Санго», горох «Афилла»)",
            GrowthStage = "Гидропонная светокультура (5-е сутки роста)",
            ScientificNotes = "Многоярусная вертикальная ферма с LED-досветкой. Критична равномерная ламинарная продувка лотков.",
            CritTempMin = 13.0,
            WarnTempMin = 17.5,
            TargetTempMin = 19.5,
            TargetTempMax = 22.5,
            WarnTempMax = 25.0,
            CritTempMax = 29.0,
            CritHumidityMin = 38.0,
            WarnHumidityMin = 50.0,
            TargetHumidityMin = 56.0,
            TargetHumidityMax = 66.0,
            WarnHumidityMax = 74.0,
            CritHumidityMax = 84.0,
            TargetCo2Min = 550.0,
            TargetCo2Max = 850.0,
            WarnCo2Max = 1200.0,
            CritCo2Max = 1600.0,
            CritVpdMin = 0.35,
            WarnVpdMin = 0.55,
            TargetVpdMin = 0.70,
            TargetVpdMax = 1.10,
            WarnVpdMax = 1.35,
            CritVpdMax = 1.75,
            MinDewPointSpreadC = 2.2,
            MaxAllowedTempDropRatePerMin = 0.50,
            MaxAllowedTempRiseRatePerMin = 0.50,
            GracePeriodSeconds = 8,
            BaseTimeToDamageMinutes = 50,
            EmergencyPlaybookSteps =
                "Проверить приточно-вытяжную установку с рекуперацией и канальные осушители|" +
                "Снизить мощность LED-светильников ярусов 3–5 до 60% при перегреве|" +
                "Проверить температуру питательного раствора в баке рециркуляции",
            LastModifiedBy = "д.с.-х.н. В.И. Солодун (Главный агроном)",
            UpdatedAtUtc = now.AddDays(-7)
        };

        var pPotato = new AgroProfile
        {
            Code = "PROF-POTATO-SEED",
            CropName = "Семенной картофель сорт «Сарма»",
            GrowthStage = "Основной период зимнего хранения семенных фондов",
            ScientificNotes = "При охлаждении ниже +1.5°C происходит осахаривание и подмораживание клубней; при T > +5°C — преждевременное пробуждение глазков.",
            CritTempMin = 1.2,
            WarnTempMin = 2.0,
            TargetTempMin = 2.6,
            TargetTempMax = 4.2,
            WarnTempMax = 5.2,
            CritTempMax = 7.5,
            CritHumidityMin = 72.0,
            WarnHumidityMin = 82.0,
            TargetHumidityMin = 86.0,
            TargetHumidityMax = 92.0,
            WarnHumidityMax = 94.5,
            CritHumidityMax = 97.5,
            TargetCo2Min = 400.0,
            TargetCo2Max = 850.0,
            WarnCo2Max = 1400.0,
            CritCo2Max = 2200.0,
            CritVpdMin = 0.02,
            WarnVpdMin = 0.04,
            TargetVpdMin = 0.06,
            TargetVpdMax = 0.14,
            WarnVpdMax = 0.20,
            CritVpdMax = 0.32,
            MinDewPointSpreadC = 0.55,
            MaxAllowedTempDropRatePerMin = 0.30,
            MaxAllowedTempRiseRatePerMin = 0.35,
            GracePeriodSeconds = 8,
            BaseTimeToDamageMinutes = 90,
            EmergencyPlaybookSteps =
                "Перевести смесительные клапаны вентиляционной камеры в режим 100% внутренней рециркуляции|" +
                "Включить антиконденсатный кабельный обогрев потолочного свода хранилища|" +
                "Проверить герметичность утепленных шиберов забора наружного морозного воздуха",
            LastModifiedBy = "д.с.-х.н. В.И. Солодун (Главный агроном)",
            UpdatedAtUtc = now.AddDays(-5)
        };

        var pWheatSilo = new AgroProfile
        {
            Code = "PROF-WHEAT-SILO",
            CropName = "Пшеница яровая «Ирень» (семенная элита)",
            GrowthStage = "Сухое силосное хранение — контроль самосогревания насыпи",
            ScientificNotes = "Ранний признак гнездового самосогревания зерна: одновременный рост температуры ядра насыпи, межзерновой влажности и выделения дыхательного CO₂.",
            CritTempMin = -5.0,
            WarnTempMin = 2.0,
            TargetTempMin = 6.5,
            TargetTempMax = 12.5,
            WarnTempMax = 17.5,
            CritTempMax = 24.0,
            CritHumidityMin = 35.0,
            WarnHumidityMin = 46.0,
            TargetHumidityMin = 52.0,
            TargetHumidityMax = 64.0,
            WarnHumidityMax = 70.0,
            CritHumidityMax = 78.0,
            TargetCo2Min = 390.0,
            TargetCo2Max = 620.0,
            WarnCo2Max = 950.0,
            CritCo2Max = 1500.0,
            CritVpdMin = 0.20,
            WarnVpdMin = 0.32,
            TargetVpdMin = 0.40,
            TargetVpdMax = 0.78,
            WarnVpdMax = 0.95,
            CritVpdMax = 1.35,
            MinDewPointSpreadC = 2.5,
            MaxAllowedTempDropRatePerMin = 0.60,
            MaxAllowedTempRiseRatePerMin = 0.40,
            GracePeriodSeconds = 6,
            BaseTimeToDamageMinutes = 60,
            EmergencyPlaybookSteps =
                "Локализовать глубину очага самосогревания по термоподвескам силоса №2|" +
                "Включить принудительную активную вентиляцию силоса холодным сухим наружным воздухом|" +
                "Отобрать пробу зерна щупом для экспресс-анализа влажности и зараженности грибами хранения|" +
                "При росте T > 28°C подготовить перекачку партии зерна на зерносушильный комплекс",
            LastModifiedBy = "Кафедра хранения и переработки с.-х. продукции ИрГАУ",
            UpdatedAtUtc = now.AddDays(-8)
        };

        var pBioLab = new AgroProfile
        {
            Code = "PROF-BIOLAB-INVITRO",
            CropName = "Микроклоны картофеля и жимолости in vitro",
            GrowthStage = "Асептическое культивирование меристем в фитотроне",
            ScientificNotes = "Прецизионная климатическая камера биолаборатории ИрГАУ. Высокая чувствительность культуральных сосудов к перепадам температуры.",
            CritTempMin = 17.0,
            WarnTempMin = 20.5,
            TargetTempMin = 22.0,
            TargetTempMax = 24.2,
            WarnTempMax = 25.8,
            CritTempMax = 28.0,
            CritHumidityMin = 45.0,
            WarnHumidityMin = 58.0,
            TargetHumidityMin = 64.0,
            TargetHumidityMax = 72.0,
            WarnHumidityMax = 78.0,
            CritHumidityMax = 86.0,
            TargetCo2Min = 400.0,
            TargetCo2Max = 650.0,
            WarnCo2Max = 950.0,
            CritCo2Max = 1350.0,
            CritVpdMin = 0.42,
            WarnVpdMin = 0.60,
            TargetVpdMin = 0.72,
            TargetVpdMax = 0.98,
            WarnVpdMax = 1.18,
            CritVpdMax = 1.45,
            MinDewPointSpreadC = 2.0,
            MaxAllowedTempDropRatePerMin = 0.35,
            MaxAllowedTempRiseRatePerMin = 0.35,
            GracePeriodSeconds = 6,
            BaseTimeToDamageMinutes = 25,
            EmergencyPlaybookSteps =
                "Проверить питание компрессора Пельтье/холодильного агрегата камеры КК-4 и ИБП|" +
                "Заблокировать открытие внешней двери чистого бокса ламинарного зала|" +
                "При отказе термостата переместить штативы с пробирками в резервную камеру КК-2",
            LastModifiedBy = "д.с.-х.н. В.И. Солодун (Научный руководитель)",
            UpdatedAtUtc = now.AddDays(-3)
        };

        _db.AgroProfiles.AddRange(pTomato, pMicrogreens, pPotato, pWheatSilo, pBioLab);
        await _db.SaveChangesAsync(ct);

        // Создаем реальную ревизию в SQL Server Temporal Table (SYSTEM_VERSIONING = ON) для демонстрации истории аудита со старта
        await Task.Delay(75, ct);
        pTomato.CritTempMin = 12.5;
        pTomato.MaxAllowedTempDropRatePerMin = 0.40;
        pTomato.LastModifiedBy = "д.с.-х.н. В.И. Солодун (зимняя корректировка Прибайкалья)";
        pTomato.UpdatedAtUtc = DateTime.UtcNow;

        pWheatSilo.CritCo2Max = 1450.0;
        pWheatSilo.LastModifiedBy = "инж. А.С. Верхозин (калибровка газоанализаторов SCD41)";
        pWheatSilo.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // 2. Пять объектов и климатических зон ИрГАУ
        var zones = new List<ClimateZone>
        {
            CreateInitialZone("GH-TOMATO-01", "Теплица №1 — Рассада томата", "Учхоз «Оёкское» ИрГАУ, зимний блок А",
                FacilityType.Greenhouse, "Зимняя поликарбонатная теплица с водяным контуром и натриевой/LED досветкой для выращивания рассады томата.",
                640, "м²", pTomato, "ESP32-GH1-01", 14, 23.1, 67.0, 780, now),

            CreateInitialZone("CF-MICRO-02", "Сити-ферма микрозелени", "Агробиотехнопарк ИрГАУ (п. Молодёжный)",
                FacilityType.CityFarm, "Пятиярусная вертикальная гидропонная установка закрытого типа с автоматическим контролем VPD и спектра.",
                180, "м²", pMicrogreens, "ESP32-CF2-02", 14, 21.0, 61.5, 690, now),

            CreateInitialZone("STOR-POTATO-03", "Овощехранилище семенного картофеля", "Хранилищный комплекс учхоза ИрГАУ, секция №3",
                FacilityType.VegetableStorage, "Навальное хранилище элитного семенного картофеля «Сарма» с подпольной активной вентиляцией.",
                1200, "т", pPotato, "ESP32-POTATO-03", 12, 3.4, 89.2, 560, now),

            CreateInitialZone("SILO-WHEAT-02", "Зерносклад учхоза — Силос пшеницы №2", "Элеваторный участок учхоза ИрГАУ",
                FacilityType.GrainSilo, "Металлический вентилируемый силос с многоуровневыми термоподвесками и датчиками межзернового CO₂ для раннего выявления самосогревания.",
                850, "т", pWheatSilo, "STM32-SILO2-04", 14, 9.6, 58.0, 465, now),

            CreateInitialZone("LAB-CHAMBER-04", "Климатическая камера биолаборатории КК-4", "Лаборатория клонального микроразмножения ИрГАУ, ауд. 118",
                FacilityType.BioLabChamber, "Прецизионная фитотронная камера для безвирусного оздоровления меристем картофеля и ягодных культур Сибири.",
                24, "м³", pBioLab, "ESP32-LAB4-05", 14, 23.2, 68.0, 495, now)
        };

        _db.ClimateZones.AddRange(zones);
        await _db.SaveChangesAsync(ct);

        // 3. Пакетная загрузка 48 часов истории измерений через SqlBulkCopy в таблицу с NONCLUSTERED COLUMNSTORE INDEX
        long insertedHistoryRows = await SeedHistoricalTelemetryBulkAsync(zones, now, ct);

        // 4. Два завершенных учебных инцидента с полной историей War Room для аналитики и Postmortem PDF
        SeedHistoricalIncidents(zones, now);
        await _db.SaveChangesAsync(ct);

        // 5. Готовые API-ключи для подключения микроконтроллеров ESP32 / Arduino студентов и инженеров ИрГАУ
        _db.ApiKeyCredentials.AddRange(
            new ApiKeyCredential
            {
                DeviceLabel = "ESP32-S3 Узел Теплицы №1 (DHT22 + SCD41)",
                ZoneId = zones[0].Id,
                KeyPrefix = "IRGAU-ESP32-GH",
                ApiKey = "IRGAU-ESP32-GH1-DEMO-2026",
                IsActive = true,
                CreatedBy = "инж. А.С. Верхозин (КИПиА)",
                CreatedAtUtc = now.AddDays(-14),
                LastUsedAtUtc = now.AddMinutes(-18),
                TotalPacketsReceived = 1420
            },
            new ApiKeyCredential
            {
                DeviceLabel = "STM32 + LoRa Термоподвеска Силоса №2",
                ZoneId = zones[3].Id,
                KeyPrefix = "IRGAU-STM32-SI",
                ApiKey = "IRGAU-STM32-SILO2-LAB-2026",
                IsActive = true,
                CreatedBy = "инж. А.С. Верхозин (КИПиА)",
                CreatedAtUtc = now.AddDays(-9),
                LastUsedAtUtc = now.AddHours(-2),
                TotalPacketsReceived = 865
            },
            new ApiKeyCredential
            {
                DeviceLabel = "Студенческий стенд ESP32 (Фитотрон ауд. 118)",
                ZoneId = zones[4].Id,
                KeyPrefix = "IRGAU-BIO-CHAM",
                ApiKey = "IRGAU-BIO-CHAMBER4-KEY-2026",
                IsActive = true,
                CreatedBy = "м.н.с. Е.А. Байкалова (Лаборатория)",
                CreatedAtUtc = now.AddDays(-4),
                LastUsedAtUtc = now.AddHours(-6),
                TotalPacketsReceived = 312
            });

        var finishedAt = DateTime.UtcNow;
        foreach (var z in zones)
        {
            z.LastTelemetryUtc = finishedAt;
        }

        await _db.SaveChangesAsync(ct);
        _channel.RecordBatchFlushed(0, 0, insertedHistoryRows);

        _logger.LogInformation("Сидирование IrGau_AgroClimateDb завершено успешно: 5 объектов ИрГАУ, {Rows} исторических записей в Columnstore.", insertedHistoryRows);
    }

    private static ClimateZone CreateInitialZone(
        string code,
        string name,
        string campus,
        FacilityType type,
        string description,
        double area,
        string unit,
        AgroProfile profile,
        string controllerId,
        int timeoutSec,
        double temp,
        double hum,
        double co2,
        DateTime nowUtc)
    {
        double dp = AgroPhysicsCalculator.CalculateDewPointC(temp, hum);
        double vpd = AgroPhysicsCalculator.CalculateVpdKpa(temp, hum);

        return new ClimateZone
        {
            Code = code,
            Name = name,
            LocationCampus = campus,
            FacilityType = type,
            Description = description,
            AreaOrVolume = area,
            AreaUnit = unit,
            AgroProfileId = profile.Id,
            ControllerNodeId = controllerId,
            HeartbeatTimeoutSeconds = timeoutSec,
            LastTelemetryUtc = nowUtc,
            IsSensorOnline = true,
            CurrentTempC = temp,
            CurrentHumidityPercent = hum,
            CurrentCo2Ppm = co2,
            CurrentDewPointC = dp,
            CurrentVpdKpa = vpd,
            CurrentTempVelocityPerMin = 0.0,
            CurrentComplianceState = ComplianceState.Optimal,
            StatusNote = "Микроклимат стабилен, все показатели в целевом зеленом коридоре"
        };
    }

    private async Task<long> SeedHistoricalTelemetryBulkAsync(List<ClimateZone> zones, DateTime nowUtc, CancellationToken ct)
    {
        var table = TelemetryBatchIngestionWorker.BuildBulkDataTable();
        var rng = new Random(20261118);

        // 48 часов с шагом 3 минуты = 960 измерений на каждую зону (4 800 строк суммарно)
        var startUtc = nowUtc.AddHours(-48);
        int totalSteps = 960;

        for (int step = 0; step <= totalSteps; step++)
        {
            var tUtc = startUtc.AddMinutes(step * 3);
            double hoursAgo = (nowUtc - tUtc).TotalHours;
            double dayPhase = Math.Sin(2.0 * Math.PI * (step / 480.0));

            foreach (var zone in zones)
            {
                var p = zone.AgroProfile!;
                double baseTemp = (p.TargetTempMin + p.TargetTempMax) * 0.5;
                double baseHum = (p.TargetHumidityMin + p.TargetHumidityMax) * 0.5;
                double baseCo2 = (p.TargetCo2Min + p.TargetCo2Max) * 0.5;

                double temp = baseTemp + dayPhase * ((p.TargetTempMax - p.TargetTempMin) * 0.24) + (rng.NextDouble() - 0.5) * 0.35;
                double hum = baseHum - dayPhase * 2.1 + (rng.NextDouble() - 0.5) * 1.2;
                double co2 = baseCo2 + dayPhase * 45.0 + (rng.NextDouble() - 0.5) * 24.0;

                // Историческая аномалия №1 (~29 часов назад в Теплице №1 — кратковременная просадка давления в теплосети)
                if (zone.Code == "GH-TOMATO-01" && hoursAgo is >= 28.4 and <= 29.3)
                {
                    temp = 15.8 + (rng.NextDouble() - 0.5) * 0.8;
                    hum = 83.5 + (rng.NextDouble() - 0.5) * 1.5;
                }

                // Историческая аномалия №2 (~11 часов назад в Овощехранилище №3 — риск конденсата при оттепели в тамбуре)
                if (zone.Code == "STOR-POTATO-03" && hoursAgo is >= 10.6 and <= 11.4)
                {
                    temp = 4.9 + (rng.NextDouble() - 0.5) * 0.3;
                    hum = 95.4 + (rng.NextDouble() - 0.5) * 0.6;
                }

                // Небольшой рабочий предупредительный выход в силосе ~20 часов назад
                if (zone.Code == "SILO-WHEAT-02" && hoursAgo is >= 19.5 and <= 20.1)
                {
                    temp = 18.2;
                    co2 = 1020;
                }

                temp = Math.Round(temp, 2);
                hum = Math.Round(Math.Clamp(hum, 15.0, 99.0), 1);
                co2 = Math.Round(Math.Clamp(co2, 360.0, 4000.0), 0);
                double dp = AgroPhysicsCalculator.CalculateDewPointC(temp, hum);
                double vpd = AgroPhysicsCalculator.CalculateVpdKpa(temp, hum);

                var eval = AgroPhysicsCalculator.EvaluateCompliance(temp, hum, co2, dp, vpd, 0.0, p);

                var row = table.NewRow();
                row["ZoneId"] = zone.Id;
                row["SensorCode"] = zone.ControllerNodeId;
                row["RecordedAtUtc"] = tUtc;
                row["TemperatureC"] = temp;
                row["HumidityPercent"] = hum;
                row["Co2Ppm"] = co2;
                row["DewPointC"] = dp;
                row["VpdKpa"] = vpd;
                row["IsSimulated"] = true;
                row["ComplianceState"] = (int)eval.ComplianceState;
                table.Rows.Add(row);
            }
        }

        var connStr = _configuration.GetConnectionString("DefaultConnection")!;
        await using var sqlConn = new SqlConnection(connStr);
        await sqlConn.OpenAsync(ct);

        using var bulkCopy = new SqlBulkCopy(sqlConn, SqlBulkCopyOptions.CheckConstraints, null)
        {
            DestinationTableName = "dbo.TelemetryReadings",
            BatchSize = 2500,
            BulkCopyTimeout = 60
        };

        bulkCopy.ColumnMappings.Add("ZoneId", "ZoneId");
        bulkCopy.ColumnMappings.Add("SensorCode", "SensorCode");
        bulkCopy.ColumnMappings.Add("RecordedAtUtc", "RecordedAtUtc");
        bulkCopy.ColumnMappings.Add("TemperatureC", "TemperatureC");
        bulkCopy.ColumnMappings.Add("HumidityPercent", "HumidityPercent");
        bulkCopy.ColumnMappings.Add("Co2Ppm", "Co2Ppm");
        bulkCopy.ColumnMappings.Add("DewPointC", "DewPointC");
        bulkCopy.ColumnMappings.Add("VpdKpa", "VpdKpa");
        bulkCopy.ColumnMappings.Add("IsSimulated", "IsSimulated");
        bulkCopy.ColumnMappings.Add("ComplianceState", "ComplianceState");

        await bulkCopy.WriteToServerAsync(table, ct);
        return table.Rows.Count;
    }

    private void SeedHistoricalIncidents(List<ClimateZone> zones, DateTime nowUtc)
    {
        var ghZone = zones[0];
        var t1Open = nowUtc.AddHours(-29.2);
        var inc1 = Incident.OpenNew(
            zoneId: ghZone.Id,
            incidentNumber: "INC-2026-0001",
            title: "Падение температуры теплоносителя при ночном морозе -31°C — Теплица №1",
            summary: "Стремительное падение температуры -0.62 °C/мин; T=15.4°C (ниже порога внимания 18.0°C), RH=84.1%",
            severity: IncidentSeverity.Critical,
            triggerType: IncidentTriggerType.RapidTrendAnomaly,
            tempC: 15.4,
            humidityPercent: 84.1,
            co2Ppm: 810,
            dewPointC: AgroPhysicsCalculator.CalculateDewPointC(15.4, 84.1),
            vpdKpa: AgroPhysicsCalculator.CalculateVpdKpa(15.4, 84.1),
            estimatedMinutesToDamage: 22,
            playbookSteps: ghZone.AgroProfile!.GetPlaybookItems(),
            nowUtc: t1Open);

        inc1.PeakDeviationValue = 14.9;
        inc1.Acknowledge("А.С. Верхозин", "Дежурный инженер КИПиА", t1Open.AddMinutes(3));
        foreach (var item in inc1.ChecklistItems)
        {
            item.IsCompleted = true;
            item.CompletedAtUtc = t1Open.AddMinutes(6 + item.StepOrder * 3);
            item.CompletedBy = "А.С. Верхозин (Дежурный инженер КИПиА)";
        }
        inc1.Status = IncidentStatus.Mitigating;
        inc1.AddComment("А.С. Верхозин", "Дежурный инженер КИПиА",
            "Зафиксировано завоздушивание грязевика на вводе теплотрассы блока А. Запущен резервный дизельный теплогенератор ТГ-2, термоэкран закрыт.",
            t1Open.AddMinutes(11));
        inc1.AddComment("В.И. Солодун", "Главный агроном / Научный руководитель",
            "Осмотр рассады томата проведен: тургор листа сохранен, точки конденсации устранены обдувом.",
            t1Open.AddMinutes(28));
        inc1.Resolve(
            "А.С. Верхозин",
            "Дежурный инженер КИПиА",
            "Падение перепада давления в основном контуре из-за засорения сетчатого фильтра перед циркуляционным насосом Grundfos.",
            "Переключен резервный насос, промыт фильтр грубой очистки, температура в рассадном отделении стабилизирована на +22.8°C.",
            t1Open.AddMinutes(39));

        var potatoZone = zones[2];
        var t2Open = nowUtc.AddHours(-11.3);
        var inc2 = Incident.OpenNew(
            zoneId: potatoZone.Id,
            incidentNumber: "INC-2026-0002",
            title: "Угроза выпадения конденсата на верхнем слое насыпи картофеля — Овощехранилище №3",
            summary: "Риск выпадения конденсата/фитопатогенов: Δ(T−Tdp)=0.4°C при T=4.9°C и RH=95.6%",
            severity: IncidentSeverity.Warning,
            triggerType: IncidentTriggerType.CondensationBlightRisk,
            tempC: 4.9,
            humidityPercent: 95.6,
            co2Ppm: 690,
            dewPointC: AgroPhysicsCalculator.CalculateDewPointC(4.9, 95.6),
            vpdKpa: AgroPhysicsCalculator.CalculateVpdKpa(4.9, 95.6),
            estimatedMinutesToDamage: 75,
            playbookSteps: potatoZone.AgroProfile!.GetPlaybookItems(),
            nowUtc: t2Open);

        inc2.PeakDeviationValue = 5.1;
        inc2.Acknowledge("Е.А. Байкалова", "Лаборант", t2Open.AddMinutes(4));
        inc2.ChecklistItems[0].IsCompleted = true;
        inc2.ChecklistItems[0].CompletedAtUtc = t2Open.AddMinutes(9);
        inc2.ChecklistItems[0].CompletedBy = "А.С. Верхозин (Дежурный инженер КИПиА)";
        inc2.ChecklistItems[1].IsCompleted = true;
        inc2.ChecklistItems[1].CompletedAtUtc = t2Open.AddMinutes(14);
        inc2.ChecklistItems[1].CompletedBy = "А.С. Верхозин (Дежурный инженер КИПиА)";
        inc2.AddComment("Е.А. Байкалова", "Лаборант",
            "После разгрузки партии тары в тамбуре повысилась влажность воздуха над секцией №3.",
            t2Open.AddMinutes(7));
        inc2.Resolve(
            "А.С. Верхозин",
            "Дежурный инженер КИПиА",
            "Неполное закрытие тамбурных ворот при погрузочных работах вызвало приток влажного воздуха.",
            "Включен антиконденсатный обогрев потолочной зоны и продувка верхнего горизонта, влажность снижена до целевых 89%.",
            t2Open.AddMinutes(31));

        _db.Incidents.AddRange(inc1, inc2);
    }
}
