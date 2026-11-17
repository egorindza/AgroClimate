using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using AgroClimate.Domain.Entities;
using AgroClimate.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgroClimate.Infrastructure.Reporting;

/// <summary>
/// Генератор официального инженерно-агрономического Postmortem PDF-акта расследования инцидента на базе QuestPDF.
/// Включает исторический срез норм из Temporal Table (SYSTEM_VERSIONING = ON) на момент открытия аварии.
/// </summary>
public sealed class PostmortemPdfService : IPostmortemPdfService
{
    private readonly IIncidentService _incidentService;
    private readonly ITelemetryAnalyticsService _analyticsService;

    static PostmortemPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PostmortemPdfService(
        IIncidentService incidentService,
        ITelemetryAnalyticsService analyticsService)
    {
        _incidentService = incidentService;
        _analyticsService = analyticsService;
    }

    public async Task<(byte[] PdfBytes, string FileName)> GeneratePostmortemPdfAsync(int incidentId, CancellationToken ct = default)
    {
        var incident = await _incidentService.GetIncidentWithDetailsAsync(incidentId, ct)
            ?? throw new KeyNotFoundException($"Инцидент #{incidentId} не найден.");

        AgroProfileTemporalHistoryDto? historicalProfile = null;
        if (incident.Zone is not null)
        {
            historicalProfile = await _analyticsService.GetProfileSnapshotAsOfAsync(
                incident.Zone.AgroProfileId,
                incident.OpenedAtUtc,
                ct);
        }

        byte[] pdfBytes = RenderDocument(incident, historicalProfile);
        string safeNum = incident.IncidentNumber.Replace(' ', '_');
        return (pdfBytes, $"Postmortem_{safeNum}_IrGAU.pdf");
    }

    public static byte[] RenderDocument(Incident incident, AgroProfileTemporalHistoryDto? temporalProfile)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var irkutskOffset = TimeSpan.FromHours(8); // Иркутское время UTC+8
        string FormatIrkutsk(DateTime? utc) =>
            utc.HasValue ? utc.Value.Add(irkutskOffset).ToString("dd.MM.yyyy HH:mm:ss") + " (ИРК)" : "—";

        var orderedEvents = incident.TimelineEvents
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();

        var orderedChecklist = incident.ChecklistItems
            .OrderBy(c => c.StepOrder)
            .ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9.5f).FontColor("#0F172A"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("ФГБОУ ВО «ИРКУТСКИЙ ГОСУДАРСТВЕННЫЙ АГРАРНЫЙ УНИВЕРСИТЕТ ИМ. А.А. ЕЖЕВСКОГО»")
                                .FontSize(8.5f).SemiBold().FontColor("#047857");
                            left.Item().Text("Служба микроклиматического мониторинга и управления инцидентами «AgroClimate War Room»")
                                .FontSize(8f).FontColor("#475569");
                            left.Item().PaddingTop(4).Text($"АКТ РАССЛЕДОВАНИЯ ИНЦИДЕНТА (POSTMORTEM) № {incident.IncidentNumber}")
                                .FontSize(13.5f).Bold().FontColor("#0F172A");
                        });

                        row.ConstantItem(120).AlignRight().Border(1).BorderColor("#CBD5E1").Background("#F8FAFC").Padding(6).Column(badge =>
                        {
                            badge.Item().Text("СТАТУС АКТА").FontSize(7f).SemiBold().FontColor("#64748B");
                            badge.Item().Text(TranslateStatus(incident.Status)).FontSize(9.5f).Bold().FontColor(
                                incident.Status == IncidentStatus.Resolved ? "#047857" : "#B91C1C");
                            badge.Item().Text($"Критичность: {TranslateSeverity(incident.Severity)}")
                                .FontSize(8f).FontColor("#334155");
                        });
                    });

                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#047857");
                });

                page.Content().PaddingVertical(10).Column(content =>
                {
                    content.Spacing(10);

                    // 1. Паспорт инцидента
                    content.Item().Background("#F8FAFC").Border(1).BorderColor("#E2E8F0").Padding(10).Column(passport =>
                    {
                        passport.Spacing(4);
                        passport.Item().Text("1. ПАСПОРТ КЛИМАТИЧЕСКОГО ИНЦИДЕНТА").FontSize(10f).Bold().FontColor("#0F172A");

                        passport.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(155);
                                cols.RelativeColumn();
                            });

                            void AddRow(string label, string value)
                            {
                                table.Cell().PaddingVertical(2).Text(label).SemiBold().FontColor("#475569");
                                table.Cell().PaddingVertical(2).Text(value);
                            }

                            AddRow("Объект / Климатическая зона:", incident.Zone?.Name ?? $"Зона #{incident.ZoneId}");
                            AddRow("Площадка / Кампус:", incident.Zone?.LocationCampus ?? "Учхоз ИрГАУ (Иркутская обл.)");
                            AddRow("Аппаратный узел телеметрии:", incident.Zone?.ControllerNodeId ?? "ESP32-IRGAU");
                            AddRow("Класс триггера тревоги:", TranslateTrigger(incident.TriggerType));
                            AddRow("Тема инцидента:", incident.Title);
                            AddRow("Первичная сводка автоматики:", incident.AlertSummary);
                            AddRow("Открыт (фиксация аномалии):", FormatIrkutsk(incident.OpenedAtUtc));
                            AddRow("Принят в работу (Acknowledge):",
                                incident.AcknowledgedAtUtc.HasValue
                                    ? $"{FormatIrkutsk(incident.AcknowledgedAtUtc)} — {incident.AcknowledgedBy} (MTTA: {incident.MttaMinutes:F1} мин)"
                                    : "Ожидает подтверждения");
                            AddRow("Закрыт / Нормализован:",
                                incident.ResolvedAtUtc.HasValue
                                    ? $"{FormatIrkutsk(incident.ResolvedAtUtc)} — {incident.ResolvedBy} (MTTR: {incident.MttrMinutes:F1} мин)"
                                    : "Инцидент еще находится в оперативной работе");
                        });
                    });

                    // 2. Показания датчиков и исторический аудит агро-профиля (SQL Temporal Table AS OF)
                    content.Item().Row(row =>
                    {
                        row.Spacing(10);

                        row.RelativeItem().Border(1).BorderColor("#E2E8F0").Padding(8).Column(m =>
                        {
                            m.Spacing(3);
                            m.Item().Text("2. ЗАМЕРЫ НА МОМЕНТ АВАРИИ").FontSize(9.5f).Bold().FontColor("#0F172A");
                            m.Item().Text($"• Температура воздуха: {incident.InitialTempC:F1} °C (пик: {incident.PeakDeviationValue:F1} °C)");
                            m.Item().Text($"• Относительная влажность: {incident.InitialHumidityPercent:F1} %");
                            m.Item().Text($"• Концентрация CO₂: {incident.InitialCo2Ppm:F0} ppm");
                            m.Item().Text($"• Расчетная точка росы (Tdp): {incident.InitialDewPointC:F2} °C");
                            m.Item().Text($"• Дефицит упругости пара (VPD): {incident.InitialVpdKpa:F3} кПа");
                            m.Item().Text($"• Оценка запаса времени до порчи: {incident.EstimatedMinutesToDamage} мин");
                        });

                        row.RelativeItem().Border(1).BorderColor("#A7F3D0").Background("#ECFDF5").Padding(8).Column(tp =>
                        {
                            tp.Spacing(3);
                            tp.Item().Text("3. НОРМЫ TEMPORAL TABLE (SYSTEM_VERSIONING)").FontSize(9.5f).Bold().FontColor("#065F46");
                            if (temporalProfile is not null)
                            {
                                tp.Item().Text($"Культура: {temporalProfile.CropName} ({temporalProfile.GrowthStage})").SemiBold();
                                tp.Item().Text($"Целевой коридор T°: {temporalProfile.TargetTempMin:F1}..{temporalProfile.TargetTempMax:F1} °C (крит. {temporalProfile.CritTempMin:F1}..{temporalProfile.CritTempMax:F1} °C)");
                                tp.Item().Text($"Целевая влажность RH: {temporalProfile.TargetHumidityMin:F0}..{temporalProfile.TargetHumidityMax:F0} %");
                                tp.Item().Text($"Целевой VPD: {temporalProfile.TargetVpdMin:F2}..{temporalProfile.TargetVpdMax:F2} кПа | Крит. CO₂: {temporalProfile.CritCo2Max:F0} ppm");
                                tp.Item().Text($"Grace Period: {temporalProfile.GracePeriodSeconds} с | Утвердил: {temporalProfile.LastModifiedBy}")
                                    .FontSize(8f).FontColor("#047857");
                            }
                            else if (incident.Zone?.AgroProfile is not null)
                            {
                                var p = incident.Zone.AgroProfile;
                                tp.Item().Text($"Культура: {p.CropName} ({p.GrowthStage})").SemiBold();
                                tp.Item().Text($"Целевой коридор T°: {p.TargetTempMin:F1}..{p.TargetTempMax:F1} °C");
                                tp.Item().Text($"Целевая влажность RH: {p.TargetHumidityMin:F0}..{p.TargetHumidityMax:F0} %");
                            }
                            else
                            {
                                tp.Item().Text("Историческая ревизия профиля не привязана.");
                            }
                        });
                    });

                    // 3. Исполнение чек-листа экстренного регламента
                    content.Item().Border(1).BorderColor("#E2E8F0").Padding(8).Column(cl =>
                    {
                        cl.Spacing(4);
                        cl.Item().Text($"4. ВЫПОЛНЕНИЕ АВАРИЙНОГО РЕГЛАМЕНТА ({incident.ChecklistCompletionPercent}%)")
                            .FontSize(10f).Bold().FontColor("#0F172A");

                        foreach (var item in orderedChecklist)
                        {
                            string mark = item.IsCompleted ? "[ВЫПОЛНЕНО]" : "[НЕ ОТМЕЧЕНО]";
                            string suffix = item.IsCompleted && item.CompletedAtUtc.HasValue
                                ? $" — {item.CompletedBy}, {FormatIrkutsk(item.CompletedAtUtc)}"
                                : string.Empty;

                            cl.Item().Text($"{item.StepOrder}. {mark} {item.Instruction}{suffix}")
                                .FontSize(8.8f)
                                .FontColor(item.IsCompleted ? "#065F46" : "#64748B");
                        }
                    });

                    // 4. Первопричина и итоги устранения (Root Cause Analysis)
                    content.Item().Background("#FFFBEB").Border(1).BorderColor("#FDE68A").Padding(8).Column(rca =>
                    {
                        rca.Spacing(4);
                        rca.Item().Text("5. УСТАНОВЛЕННАЯ ПЕРВОПРИЧИНА (ROOT CAUSE) И ВЫВОДЫ КОМИССИИ")
                            .FontSize(10f).Bold().FontColor("#92400E");
                        rca.Item().Text($"Первопричина: {incident.RootCauseAnalysis ?? "Расследование продолжается (инцидент в работе)."}")
                            .SemiBold();
                        rca.Item().Text($"Выполненные мероприятия: {incident.ResolutionSummary ?? "Выполняются оперативные корректирующие действия дежурной смены."}");
                    });

                    // 5. Полная хронология событий (Timeline)
                    content.Item().Column(tl =>
                    {
                        tl.Spacing(4);
                        tl.Item().Text("6. ЖУРНАЛ ОПЕРАТИВНЫХ ДЕЙСТВИЙ СМЕНЫ (WAR ROOM TIMELINE)")
                            .FontSize(10f).Bold().FontColor("#0F172A");

                        tl.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(118);
                                cols.ConstantColumn(125);
                                cols.RelativeColumn();
                            });

                            table.Header(h =>
                            {
                                h.Cell().Background("#E2E8F0").Padding(4).Text("Время (Иркутск)").FontSize(8f).Bold();
                                h.Cell().Background("#E2E8F0").Padding(4).Text("Субъект / Роль").FontSize(8f).Bold();
                                h.Cell().Background("#E2E8F0").Padding(4).Text("Событие / Оперативное действие").FontSize(8f).Bold();
                            });

                            foreach (var ev in orderedEvents)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(4)
                                    .Text(ev.OccurredAtUtc.Add(irkutskOffset).ToString("dd.MM HH:mm:ss")).FontSize(8f);
                                table.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(4)
                                    .Text($"{ev.AuthorName} ({ev.AuthorRole})").FontSize(8f).SemiBold();
                                table.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(4)
                                    .Text(ev.Message).FontSize(8.3f);
                            }
                        });
                    });

                    // 6. Подписи должностных лиц ИрГАУ
                    content.Item().PaddingTop(14).Row(sig =>
                    {
                        sig.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Дежурный инженер КИПиА: _______________________ / А.С. Верхозин /").FontSize(8.5f);
                        });
                        sig.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("Главный агроном ИрГАУ: _______________________ / В.И. Солодун /").FontSize(8.5f);
                        });
                    });
                });

                page.Footer().AlignCenter().Text(txt =>
                {
                    txt.Span("АИС «AgroClimate War Room» • ФГБОУ ВО ИрГАУ им. А.А. Ежевского • Стр. ").FontSize(8f).FontColor("#64748B");
                    txt.CurrentPageNumber().FontSize(8f).FontColor("#64748B");
                    txt.Span(" из ").FontSize(8f).FontColor("#64748B");
                    txt.TotalPages().FontSize(8f).FontColor("#64748B");
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string TranslateSeverity(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Emergency => "ЧРЕЗВЫЧАЙНАЯ",
        IncidentSeverity.Critical => "КРИТИЧЕСКАЯ",
        _ => "ПРЕДУПРЕЖДЕНИЕ"
    };

    private static string TranslateStatus(IncidentStatus status) => status switch
    {
        IncidentStatus.Resolved => "ЗАКРЫТ (УСТРАНЕН)",
        IncidentStatus.Mitigating => "УСТРАНЕНИЕ",
        IncidentStatus.Acknowledged => "В РАБОТЕ",
        _ => "ОТКРЫТ (ТРЕВОГА)"
    };

    private static string TranslateTrigger(IncidentTriggerType trigger) => trigger switch
    {
        IncidentTriggerType.DeadMansSwitch => "Dead Man's Switch (потеря связи с контроллером)",
        IncidentTriggerType.SelfHeatingRisk => "Самосогревание зерновой насыпи и рост дыхания CO₂",
        IncidentTriggerType.RapidTrendAnomaly => "Раннее выявление опасного тренда (T-SQL Window Functions)",
        IncidentTriggerType.CondensationBlightRisk => "Точка росы / угроза выпадения конденсата",
        _ => "Выход показаний за пороговые границы агро-профиля"
    };
}
