using AgroClimate.Application.DTOs;
using FluentValidation;

namespace AgroClimate.Application.Validators;

public sealed class AgroProfileUpdateDtoValidator : AbstractValidator<AgroProfileUpdateDto>
{
    public AgroProfileUpdateDtoValidator()
    {
        RuleFor(x => x.CropName)
            .NotEmpty().WithMessage("Укажите наименование сельскохозяйственной культуры или биоматериала.")
            .MaximumLength(120);

        RuleFor(x => x.GrowthStage)
            .NotEmpty().WithMessage("Укажите фазу роста или режим хранения.")
            .MaximumLength(120);

        // Иерархия температурных порогов: CritMin < WarnMin <= TargetMin < TargetMax <= WarnMax < CritMax
        RuleFor(x => x.CritTempMin)
            .InclusiveBetween(-50, 50)
            .LessThan(x => x.WarnTempMin)
            .WithMessage("Критический минимум температуры должен быть строго ниже предупредительного минимума.");

        RuleFor(x => x.WarnTempMin)
            .LessThanOrEqualTo(x => x.TargetTempMin)
            .WithMessage("Предупредительный минимум температуры не может превышать нижнюю границу целевого коридора.");

        RuleFor(x => x.TargetTempMin)
            .LessThan(x => x.TargetTempMax)
            .WithMessage("Нижняя граница целевой температуры должна быть меньше верхней границы.");

        RuleFor(x => x.WarnTempMax)
            .GreaterThanOrEqualTo(x => x.TargetTempMax)
            .WithMessage("Предупредительный максимум температуры не может быть ниже целевого максимума.");

        RuleFor(x => x.CritTempMax)
            .InclusiveBetween(-20, 75)
            .GreaterThan(x => x.WarnTempMax)
            .WithMessage("Критический максимум температуры должен быть строго выше предупредительного максимума.");

        // Иерархия порогов влажности (%)
        RuleFor(x => x.CritHumidityMin)
            .InclusiveBetween(5, 95)
            .LessThan(x => x.WarnHumidityMin)
            .WithMessage("Критический минимум влажности должен быть строго ниже предупредительного минимума.");

        RuleFor(x => x.WarnHumidityMin)
            .LessThanOrEqualTo(x => x.TargetHumidityMin)
            .WithMessage("Предупредительный минимум влажности не может превышать нижнюю границу нормы.");

        RuleFor(x => x.TargetHumidityMin)
            .LessThan(x => x.TargetHumidityMax)
            .WithMessage("Нижняя граница целевой влажности должна быть меньше верхней.");

        RuleFor(x => x.WarnHumidityMax)
            .GreaterThanOrEqualTo(x => x.TargetHumidityMax)
            .WithMessage("Предупредительный максимум влажности не может быть ниже целевого максимума.");

        RuleFor(x => x.CritHumidityMax)
            .InclusiveBetween(20, 100)
            .GreaterThan(x => x.WarnHumidityMax)
            .WithMessage("Критический максимум влажности должен быть строго выше предупредительного максимума.");

        // CO2 (ppm)
        RuleFor(x => x.TargetCo2Min)
            .InclusiveBetween(250, 3000)
            .LessThan(x => x.TargetCo2Max)
            .WithMessage("Целевой минимум CO₂ должен быть меньше целевого максимума.");

        RuleFor(x => x.WarnCo2Max)
            .GreaterThanOrEqualTo(x => x.TargetCo2Max)
            .WithMessage("Предупредительный порог CO₂ должен быть не ниже целевого максимума.");

        RuleFor(x => x.CritCo2Max)
            .InclusiveBetween(600, 10000)
            .GreaterThan(x => x.WarnCo2Max)
            .WithMessage("Критический порог CO₂ должен быть строго выше предупредительного порога.");

        // VPD (кПа)
        RuleFor(x => x.CritVpdMin)
            .InclusiveBetween(0.0, 4.0)
            .LessThanOrEqualTo(x => x.WarnVpdMin)
            .WithMessage("Критический минимум VPD не может превышать предупредительный минимум.");

        RuleFor(x => x.TargetVpdMin)
            .GreaterThanOrEqualTo(x => x.WarnVpdMin)
            .LessThan(x => x.TargetVpdMax)
            .WithMessage("Целевой минимум VPD должен быть меньше целевого максимума и не ниже Warn-минимума.");

        RuleFor(x => x.CritVpdMax)
            .InclusiveBetween(0.2, 6.0)
            .GreaterThan(x => x.WarnVpdMax)
            .WithMessage("Критический максимум VPD должен быть строго выше предупредительного максимума.");

        RuleFor(x => x.MinDewPointSpreadC)
            .InclusiveBetween(0.3, 10.0)
            .WithMessage("Минимальный запас до точки росы должен находиться в диапазоне 0.3..10.0 °C.");

        RuleFor(x => x.GracePeriodSeconds)
            .InclusiveBetween(0, 3600)
            .WithMessage("Grace Period должен быть от 0 до 3600 секунд.");

        RuleFor(x => x.BaseTimeToDamageMinutes)
            .InclusiveBetween(5, 1440)
            .WithMessage("Базовый запас времени до риска порчи должен быть от 5 до 1440 минут.");

        RuleFor(x => x.EmergencyPlaybookSteps)
            .NotEmpty()
            .MinimumLength(10)
            .WithMessage("Заполните хотя бы один шаг регламента экстренных действий для дежурной смены.");
    }
}

public sealed class TelemetryIngestRequestValidator : AbstractValidator<TelemetryIngestRequest>
{
    public TelemetryIngestRequestValidator()
    {
        RuleFor(x => x.SensorCode)
            .NotEmpty().WithMessage("Поле sensorCode обязательно для идентификации узла ESP32/Arduino.")
            .MaximumLength(64);

        RuleFor(x => x.TemperatureC)
            .InclusiveBetween(-65.0, 85.0)
            .WithMessage("Температура вне физического диапазона датчика (-65..+85 °C). Проверьте линию связи датчика.");

        RuleFor(x => x.HumidityPercent)
            .InclusiveBetween(0.5, 100.0)
            .WithMessage("Относительная влажность должна находиться в пределах 0.5..100 %.");

        RuleFor(x => x.Co2Ppm)
            .InclusiveBetween(200.0, 15000.0)
            .WithMessage("Концентрация CO₂ должна находиться в пределах 200..15000 ppm.");
    }
}

public sealed class ResolveIncidentRequestValidator : AbstractValidator<ResolveIncidentRequest>
{
    public ResolveIncidentRequestValidator()
    {
        RuleFor(x => x.IncidentId).GreaterThan(0);

        RuleFor(x => x.RootCauseAnalysis)
            .NotEmpty().WithMessage("Укажите установленную первопричину инцидента (Root Cause) для акта расследования.")
            .MinimumLength(8).WithMessage("Описание первопричины должно содержать не менее 8 символов.")
            .MaximumLength(1000);

        RuleFor(x => x.ResolutionSummary)
            .NotEmpty().WithMessage("Укажите выполненные инженерно-агрономические мероприятия.")
            .MinimumLength(8).WithMessage("Итог устранения должен содержать не менее 8 символов.")
            .MaximumLength(1000);
    }
}

public sealed class AddIncidentCommentRequestValidator : AbstractValidator<AddIncidentCommentRequest>
{
    public AddIncidentCommentRequestValidator()
    {
        RuleFor(x => x.IncidentId).GreaterThan(0);
        RuleFor(x => x.Comment)
            .NotEmpty().WithMessage("Введите текст оперативного сообщения дежурной смены.")
            .MaximumLength(600);
    }
}

public sealed class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(x => x.DeviceLabel)
            .NotEmpty().WithMessage("Укажите название контроллера или лабораторного стенда (например, ESP32-S3 Лаб. №204).")
            .MinimumLength(3)
            .MaximumLength(100);

        RuleFor(x => x.ZoneId)
            .GreaterThan(0).WithMessage("Выберите климатическую зону привязки датчика.");
    }
}
