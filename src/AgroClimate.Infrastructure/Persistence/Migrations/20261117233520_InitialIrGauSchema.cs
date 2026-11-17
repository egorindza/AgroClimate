using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroClimate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIrGauSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgroProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CropName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    GrowthStage = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ScientificNotes = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    TargetTempMin = table.Column<double>(type: "float", nullable: false),
                    TargetTempMax = table.Column<double>(type: "float", nullable: false),
                    WarnTempMin = table.Column<double>(type: "float", nullable: false),
                    WarnTempMax = table.Column<double>(type: "float", nullable: false),
                    CritTempMin = table.Column<double>(type: "float", nullable: false),
                    CritTempMax = table.Column<double>(type: "float", nullable: false),
                    TargetHumidityMin = table.Column<double>(type: "float", nullable: false),
                    TargetHumidityMax = table.Column<double>(type: "float", nullable: false),
                    WarnHumidityMin = table.Column<double>(type: "float", nullable: false),
                    WarnHumidityMax = table.Column<double>(type: "float", nullable: false),
                    CritHumidityMin = table.Column<double>(type: "float", nullable: false),
                    CritHumidityMax = table.Column<double>(type: "float", nullable: false),
                    TargetCo2Min = table.Column<double>(type: "float", nullable: false),
                    TargetCo2Max = table.Column<double>(type: "float", nullable: false),
                    WarnCo2Max = table.Column<double>(type: "float", nullable: false),
                    CritCo2Max = table.Column<double>(type: "float", nullable: false),
                    TargetVpdMin = table.Column<double>(type: "float", nullable: false),
                    TargetVpdMax = table.Column<double>(type: "float", nullable: false),
                    WarnVpdMin = table.Column<double>(type: "float", nullable: false),
                    WarnVpdMax = table.Column<double>(type: "float", nullable: false),
                    CritVpdMin = table.Column<double>(type: "float", nullable: false),
                    CritVpdMax = table.Column<double>(type: "float", nullable: false),
                    MinDewPointSpreadC = table.Column<double>(type: "float", nullable: false),
                    MaxAllowedTempDropRatePerMin = table.Column<double>(type: "float", nullable: false),
                    MaxAllowedTempRiseRatePerMin = table.Column<double>(type: "float", nullable: false),
                    GracePeriodSeconds = table.Column<int>(type: "int", nullable: false),
                    BaseTimeToDamageMinutes = table.Column<int>(type: "int", nullable: false),
                    EmergencyPlaybookSteps = table.Column<string>(type: "nvarchar(2500)", maxLength: 2500, nullable: false),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidToUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgroProfiles", x => x.Id);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "AgroProfilesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidToUtc")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFromUtc");

            migrationBuilder.CreateTable(
                name: "ClimateZones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    LocationCampus = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FacilityType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    AreaOrVolume = table.Column<double>(type: "float", nullable: false),
                    AreaUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AgroProfileId = table.Column<int>(type: "int", nullable: false),
                    ControllerNodeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HeartbeatTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    LastTelemetryUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSensorOnline = table.Column<bool>(type: "bit", nullable: false),
                    CurrentTempC = table.Column<double>(type: "float", nullable: false),
                    CurrentHumidityPercent = table.Column<double>(type: "float", nullable: false),
                    CurrentCo2Ppm = table.Column<double>(type: "float", nullable: false),
                    CurrentDewPointC = table.Column<double>(type: "float", nullable: false),
                    CurrentVpdKpa = table.Column<double>(type: "float", nullable: false),
                    CurrentTempVelocityPerMin = table.Column<double>(type: "float", nullable: false),
                    CurrentComplianceState = table.Column<int>(type: "int", nullable: false),
                    StatusNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClimateZones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClimateZones_AgroProfiles_AgroProfileId",
                        column: x => x.AgroProfileId,
                        principalTable: "AgroProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeyCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceLabel = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalPacketsReceived = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeyCredentials_ClimateZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "ClimateZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    AlertSummary = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    RootCauseAnalysis = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    ResolutionSummary = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    InitialTempC = table.Column<double>(type: "float", nullable: false),
                    InitialHumidityPercent = table.Column<double>(type: "float", nullable: false),
                    InitialCo2Ppm = table.Column<double>(type: "float", nullable: false),
                    InitialDewPointC = table.Column<double>(type: "float", nullable: false),
                    InitialVpdKpa = table.Column<double>(type: "float", nullable: false),
                    PeakDeviationValue = table.Column<double>(type: "float", nullable: false),
                    EstimatedMinutesToDamage = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidents_ClimateZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "ClimateZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZoneId = table.Column<int>(type: "int", nullable: false),
                    SensorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TemperatureC = table.Column<double>(type: "float", nullable: false),
                    HumidityPercent = table.Column<double>(type: "float", nullable: false),
                    Co2Ppm = table.Column<double>(type: "float", nullable: false),
                    DewPointC = table.Column<double>(type: "float", nullable: false),
                    VpdKpa = table.Column<double>(type: "float", nullable: false),
                    IsSimulated = table.Column<bool>(type: "bit", nullable: false),
                    ComplianceState = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryReadings_ClimateZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "ClimateZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncidentChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentChecklistItems_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncidentTimelineEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AuthorRole = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentTimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentTimelineEvents_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgroProfiles_Code",
                table: "AgroProfiles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyCredentials_ApiKey",
                table: "ApiKeyCredentials",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyCredentials_ZoneId",
                table: "ApiKeyCredentials",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ClimateZones_AgroProfileId",
                table: "ClimateZones",
                column: "AgroProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ClimateZones_Code",
                table: "ClimateZones",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentChecklistItems_IncidentId",
                table: "IncidentChecklistItems",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_IncidentNumber",
                table: "Incidents",
                column: "IncidentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ZoneId_Status",
                table: "Incidents",
                columns: new[] { "ZoneId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentTimelineEvents_IncidentId_OccurredAtUtc",
                table: "IncidentTimelineEvents",
                columns: new[] { "IncidentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryReadings_ZoneId_RecordedAtUtc",
                table: "TelemetryReadings",
                columns: new[] { "ZoneId", "RecordedAtUtc" });

            migrationBuilder.Sql("""
                CREATE NONCLUSTERED COLUMNSTORE INDEX [NCCI_TelemetryReadings_Analytics]
                ON [dbo].[TelemetryReadings]
                (
                    [ZoneId],
                    [RecordedAtUtc],
                    [TemperatureC],
                    [HumidityPercent],
                    [Co2Ppm],
                    [DewPointC],
                    [VpdKpa],
                    [ComplianceState],
                    [IsSimulated]
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [NCCI_TelemetryReadings_Analytics] ON [dbo].[TelemetryReadings];");

            migrationBuilder.DropTable(
                name: "ApiKeyCredentials");

            migrationBuilder.DropTable(
                name: "IncidentChecklistItems");

            migrationBuilder.DropTable(
                name: "IncidentTimelineEvents");

            migrationBuilder.DropTable(
                name: "TelemetryReadings");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "ClimateZones");

            migrationBuilder.DropTable(
                name: "AgroProfiles")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "AgroProfilesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidToUtc")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFromUtc");
        }
    }
}
