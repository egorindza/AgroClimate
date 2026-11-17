using System.Text;
using AgroClimate.Application.DTOs;
using AgroClimate.Application.Validators;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using AgroClimate.Infrastructure.Pipeline;
using AgroClimate.Infrastructure.Reporting;

namespace AgroClimate.Tests;

public sealed class IncidentLifecycleAndValidationTests
{
    [Fact]
    public void IncidentLifecycle_TransitionsFromOpenToResolved_AndComputesMttaMttr()
    {
        var t0 = new DateTime(2026, 11, 18, 4, 0, 0, DateTimeKind.Utc);
        var incident = Incident.OpenNew(
            zoneId: 1,
            incidentNumber: "INC-2026-0099",
            title: "Отказ контура обогрева — Теплица №1",
            summary: "Температура упала ниже +12°C",
            severity: IncidentSeverity.Critical,
            triggerType: IncidentTriggerType.ThresholdBreach,
            tempC: 11.4,
            humidityPercent: 86.0,
            co2Ppm: 790,
            dewPointC: 9.1,
            vpdKpa: 0.19,
            estimatedMinutesToDamage: 25,
            playbookSteps: new[] { "Запустить резервный теплогенератор ТГ-2", "Опустить термоэкран" },
            nowUtc: t0);

        incident.ChecklistItems[0].Id = 101;
        incident.ChecklistItems[1].Id = 102;

        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(2, incident.ChecklistItems.Count);
        Assert.Equal(0, incident.ChecklistCompletionPercent);

        // 1. Взятие в работу через 4 минуты (MTTA = 4.0 мин)
        incident.Acknowledge("А.С. Верхозин", "Дежурный инженер КИПиА", t0.AddMinutes(4));
        Assert.Equal(IncidentStatus.Acknowledged, incident.Status);
        Assert.Equal(4.0, incident.MttaMinutes);

        // 2. Выполнение первого шага регламента -> статус Mitigating, прогресс 50%
        incident.ToggleChecklistItem(101, "А.С. Верхозин", "Дежурный инженер КИПиА", t0.AddMinutes(7));
        Assert.Equal(IncidentStatus.Mitigating, incident.Status);
        Assert.Equal(50, incident.ChecklistCompletionPercent);

        // 3. Эскалация до Emergency
        bool escalated = incident.Escalate(IncidentSeverity.Emergency, "Внешний мороз усилился до -36°C", 14, t0.AddMinutes(9));
        Assert.True(escalated);
        Assert.Equal(IncidentSeverity.Emergency, incident.Severity);
        Assert.Equal(14, incident.EstimatedMinutesToDamage);

        // 4. Комментарий смены и закрытие через 26 минут (MTTR = 26.0 мин)
        incident.AddComment("В.И. Солодун", "Главный агроном", "Рассада осмотрена, повреждений точки роста нет.", t0.AddMinutes(20));
        incident.Resolve(
            "А.С. Верхозин",
            "Дежурный инженер КИПиА",
            "Заклинивание обратного клапана сетевого насоса.",
            "Введен резервный насос, контур развоздушен.",
            t0.AddMinutes(26));

        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.False(incident.IsActive);
        Assert.Equal(26.0, incident.MttrMinutes);

        // Повторное закрытие запрещено инвариантом
        Assert.Throws<InvalidOperationException>(() =>
            incident.Resolve("А.С. Верхозин", "Инженер", "Повторно", "Повторно"));
    }

    [Fact]
    public void AgroProfileValidator_RejectsInvertedCorridors_AndAcceptsValidProfile()
    {
        var validator = new AgroProfileUpdateDtoValidator();

        var valid = new AgroProfileUpdateDto
        {
            Id = 1,
            CropName = "Томат «Сибирский скороспелый F1»",
            GrowthStage = "Рассада",
            CritTempMin = 12,
            WarnTempMin = 18,
            TargetTempMin = 21,
            TargetTempMax = 25,
            WarnTempMax = 28,
            CritTempMax = 33,
            CritHumidityMin = 40,
            WarnHumidityMin = 55,
            TargetHumidityMin = 62,
            TargetHumidityMax = 72,
            WarnHumidityMax = 80,
            CritHumidityMax = 90,
            TargetCo2Min = 600,
            TargetCo2Max = 950,
            WarnCo2Max = 1300,
            CritCo2Max = 1800,
            CritVpdMin = 0.3,
            WarnVpdMin = 0.55,
            TargetVpdMin = 0.75,
            TargetVpdMax = 1.15,
            WarnVpdMax = 1.45,
            CritVpdMax = 1.85,
            MinDewPointSpreadC = 2.0,
            MaxAllowedTempDropRatePerMin = 0.45,
            MaxAllowedTempRiseRatePerMin = 0.55,
            GracePeriodSeconds = 10,
            BaseTimeToDamageMinutes = 35,
            EmergencyPlaybookSteps = "Проверить насос|Запустить резервный теплогенератор"
        };

        Assert.True(validator.Validate(valid).IsValid);

        // Нарушаем иерархию: TargetTempMin > TargetTempMax
        valid.TargetTempMin = 27;
        var invalidResult = validator.Validate(valid);
        Assert.False(invalidResult.IsValid);
    }

    [Fact]
    public async Task TelemetryChannelBuffer_CoalescesPacketsIntoBatch()
    {
        var buffer = new TelemetryChannelBuffer();
        var now = DateTime.UtcNow;

        for (int i = 1; i <= 5; i++)
        {
            await buffer.EnqueueAsync(new TelemetryPacketEnvelope(
                ZoneId: i,
                SensorCode: $"ESP32-0{i}",
                RecordedAtUtc: now,
                TemperatureC: 22.0 + i * 0.2,
                HumidityPercent: 65.0,
                Co2Ppm: 700,
                DewPointC: 15.1,
                VpdKpa: 0.92,
                IsSimulated: true));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var batch in buffer.ReadBatchesAsync(10, TimeSpan.FromMilliseconds(30), cts.Token))
        {
            Assert.Equal(5, batch.Count);
            buffer.RecordBatchFlushed(batch.Count, 1.8, 5005);
            break;
        }

        var metrics = buffer.GetMetrics();
        Assert.Equal(5, metrics.TotalEnqueued);
        Assert.Equal(5, metrics.TotalPersistedRows);
        Assert.Equal(5005, metrics.ColumnstoreTotalRows);
    }

    [Fact]
    public void PostmortemPdfService_GeneratesValidCyrillicPdfDocument()
    {
        var zone = new ClimateZone
        {
            Id = 1,
            Code = "GH-TOMATO-01",
            Name = "Теплица №1 — Рассада томата",
            LocationCampus = "Учхоз «Оёкское» ИрГАУ",
            ControllerNodeId = "ESP32-GH1-01"
        };

        var incident = Incident.OpenNew(
            zoneId: 1,
            incidentNumber: "INC-2026-0777",
            title: "Испытание генерации акта Postmortem PDF ИрГАУ",
            summary: "Проверка кириллического рендеринга QuestPDF и среза Temporal Table",
            severity: IncidentSeverity.Critical,
            triggerType: IncidentTriggerType.RapidTrendAnomaly,
            tempC: 14.2,
            humidityPercent: 85.0,
            co2Ppm: 820,
            dewPointC: 11.7,
            vpdKpa: 0.24,
            estimatedMinutesToDamage: 20,
            playbookSteps: new[] { "Запустить резервный теплогенератор ТГ-2", "Проверить обратку контура" });

        incident.Zone = zone;
        incident.Resolve(
            "А.С. Верхозин",
            "Дежурный инженер КИПиА",
            "Тестовая просадка давления.",
            "Нормализовано автоматикой резерва.");

        var snapshot = new AgroProfileTemporalHistoryDto
        {
            Id = 1,
            Code = "PROF-TOMATO-SEEDLING",
            CropName = "Томат «Сибирский скороспелый F1»",
            GrowthStage = "Рассада",
            TargetTempMin = 21.0,
            TargetTempMax = 25.0,
            CritTempMin = 12.5,
            CritTempMax = 33.0,
            TargetHumidityMin = 62.0,
            TargetHumidityMax = 72.0,
            CritCo2Max = 1800,
            TargetVpdMin = 0.72,
            TargetVpdMax = 1.15,
            GracePeriodSeconds = 6,
            BaseTimeToDamageMinutes = 35,
            LastModifiedBy = "д.с.-х.н. В.И. Солодун",
            ValidFromUtc = DateTime.UtcNow.AddDays(-5),
            ValidToUtc = new DateTime(9999, 12, 31)
        };

        byte[] pdfBytes = PostmortemPdfService.RenderDocument(incident, snapshot);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 5_000, $"Expected PDF > 5 KB, actual: {pdfBytes.Length} bytes");

        string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }
}
