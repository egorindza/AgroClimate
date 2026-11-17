using System.Globalization;
using AgroClimate.Application;
using AgroClimate.Application.Interfaces;
using AgroClimate.Infrastructure;
using AgroClimate.Infrastructure.Seed;
using AgroClimate.Web.Hubs;
using AgroClimate.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Унифицируем точку как разделитель дробной части для корректного биндинга HTML number-полей и JSON на русской Windows
var culture = new CultureInfo("ru-RU")
{
    NumberFormat = CultureInfo.InvariantCulture.NumberFormat
};
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "IrGau.AgroClimate.DemoRole";
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/Login";
    });
builder.Services.AddAuthorization();

// Регистрируем живой SignalR-нотификатор до вызова AddInfrastructureServices
builder.Services.AddSingleton<IClimateRealtimeNotifier, SignalRClimateNotifier>();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration, enableBackgroundWorkers: true);

var app = builder.Build();

// Автоматическое применение миграций, индексов Columnstore/Temporal и наполнение демо-данными ИрГАУ
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<AgroClimateDataSeeder>();
    await seeder.SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();

// В демо-режиме портфолио назначаем роль Главного агронома по умолчанию, если cookie еще не выбрана
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.User = DemoRoles.BuildPrincipal(
            DemoRoles.AllPersonas[0],
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    await next();
});

app.UseAuthorization();

app.MapControllers();
app.MapHub<ClimateHub>("/hubs/climate");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

public partial class Program;
