using System.Security.Claims;

namespace AgroClimate.Web.Security;

public static class DemoRoles
{
    public const string ChiefAgronomist = "ChiefAgronomist";
    public const string ShiftEngineer = "ShiftEngineer";
    public const string LabAssistant = "LabAssistant";

    public sealed record DemoPersona(
        string RoleKey,
        string FullName,
        string RoleTitle,
        string Department,
        string BadgeColor,
        string CapabilitiesSummary);

    public static readonly IReadOnlyList<DemoPersona> AllPersonas = new[]
    {
        new DemoPersona(
            ChiefAgronomist,
            "д.с.-х.н. В.И. Солодун",
            "Главный агроном / Научный руководитель",
            "Кафедра агроэкологии и растениеводства ИрГАУ",
            "emerald",
            "Полный доступ: конструктор агро-профилей (Temporal Tables), управление War Room, IoT-симулятор, аналитика SLA и экспорт актов PDF."),
        new DemoPersona(
            ShiftEngineer,
            "инж. А.С. Верхозин",
            "Дежурный инженер КИПиА",
            "Служба автоматизации и телеметрии учхоза ИрГАУ",
            "amber",
            "Оперативное реагирование War Room (Acknowledge, чек-листы, закрытие аварий), выпуск API-ключей ESP32, управление симулятором и выгрузка PDF."),
        new DemoPersona(
            LabAssistant,
            "м.н.с. Е.А. Байкалова",
            "Лаборант",
            "Биолаборатория клонального микроразмножения ИрГАУ",
            "sky",
            "Мониторинг климатических зон, взятие тревог под наблюдение, ведение журнала смены в War Room, просмотр аналитики и скачивание PDF.")
    };

    public static DemoPersona FindOrDefault(string? roleKey) =>
        AllPersonas.FirstOrDefault(p => string.Equals(p.RoleKey, roleKey, StringComparison.OrdinalIgnoreCase))
        ?? AllPersonas[0];

    public static ClaimsPrincipal BuildPrincipal(DemoPersona persona, string authScheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, persona.FullName),
            new(ClaimTypes.Role, persona.RoleKey),
            new("RoleTitle", persona.RoleTitle),
            new("Department", persona.Department)
        };

        var identity = new ClaimsIdentity(claims, authScheme);
        return new ClaimsPrincipal(identity);
    }

    public static string GetFullName(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name)
            ? user.Identity.Name
            : AllPersonas[0].FullName;

    public static string GetRoleTitle(this ClaimsPrincipal user) =>
        user.FindFirstValue("RoleTitle") ?? AllPersonas[0].RoleTitle;

    public static string GetRoleKey(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role) ?? ChiefAgronomist;

    public static bool CanEditAgroProfiles(this ClaimsPrincipal user) =>
        user.GetRoleKey() == ChiefAgronomist;

    public static bool CanManageApiKeys(this ClaimsPrincipal user) =>
        user.GetRoleKey() is ChiefAgronomist or ShiftEngineer;

    public static bool CanResolveIncidents(this ClaimsPrincipal user) =>
        user.GetRoleKey() is ChiefAgronomist or ShiftEngineer;
}
