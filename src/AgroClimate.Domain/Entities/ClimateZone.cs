using AgroClimate.Domain.Enums;

namespace AgroClimate.Domain.Entities;

/// <summary>
/// Климатическая зона / производственно-научный объект ИрГАУ и агропредприятий Сибири.
/// </summary>
public class ClimateZone
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LocationCampus { get; set; } = string.Empty;
    public FacilityType FacilityType { get; set; }
    public string Description { get; set; } = string.Empty;
    public double AreaOrVolume { get; set; }
    public string AreaUnit { get; set; } = "м²";

    public int AgroProfileId { get; set; }
    public AgroProfile? AgroProfile { get; set; }

    // Параметры аппаратного контроллера и Dead Man's Switch
    public string ControllerNodeId { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = "IrGAU-TelemetryOS v2.4.1";
    public int HeartbeatTimeoutSeconds { get; set; } = 12;
    public DateTime? LastTelemetryUtc { get; set; }
    public bool IsSensorOnline { get; set; } = true;

    // Кэшированное последнее измерение для мгновенного отображения на карте объектов
    public double CurrentTempC { get; set; }
    public double CurrentHumidityPercent { get; set; }
    public double CurrentCo2Ppm { get; set; }
    public double CurrentDewPointC { get; set; }
    public double CurrentVpdKpa { get; set; }
    public double CurrentTempVelocityPerMin { get; set; }
    public ComplianceState CurrentComplianceState { get; set; } = ComplianceState.Optimal;
    public string StatusNote { get; set; } = "Микроклимат в пределах целевого коридора";

    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    public ICollection<ApiKeyCredential> ApiKeys { get; set; } = new List<ApiKeyCredential>();
}
