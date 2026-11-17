namespace AgroClimate.Domain.Entities;

/// <summary>
/// Учетная запись API-ключа для приема JSON-телеметрии от реальных контроллеров ESP32 / Arduino / STM32.
/// </summary>
public class ApiKeyCredential
{
    public int Id { get; set; }
    public string DeviceLabel { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public int ZoneId { get; set; }
    public ClimateZone? Zone { get; set; }

    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "Инженер КИПиА ИрГАУ";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
    public long TotalPacketsReceived { get; set; }
}
