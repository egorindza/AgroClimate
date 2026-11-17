using AgroClimate.Application.Services;
using AgroClimate.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AgroClimate.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AgroProfileUpdateDtoValidator>();
        services.AddSingleton<AlertEvaluationEngine>();
        return services;
    }
}
