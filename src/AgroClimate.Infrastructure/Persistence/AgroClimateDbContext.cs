using AgroClimate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgroClimate.Infrastructure.Persistence;

public sealed class AgroClimateDbContext : DbContext
{
    public AgroClimateDbContext(DbContextOptions<AgroClimateDbContext> options)
        : base(options)
    {
    }

    public DbSet<AgroProfile> AgroProfiles => Set<AgroProfile>();
    public DbSet<ClimateZone> ClimateZones => Set<ClimateZone>();
    public DbSet<TelemetryReading> TelemetryReadings => Set<TelemetryReading>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentChecklistItem> IncidentChecklistItems => Set<IncidentChecklistItem>();
    public DbSet<IncidentTimelineEvent> IncidentTimelineEvents => Set<IncidentTimelineEvent>();
    public DbSet<ApiKeyCredential> ApiKeyCredentials => Set<ApiKeyCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Temporal Table (SYSTEM_VERSIONING = ON) для аудита агро-профилей норм
        modelBuilder.Entity<AgroProfile>(b =>
        {
            b.ToTable("AgroProfiles", tb => tb.IsTemporal(ttb =>
            {
                ttb.HasPeriodStart("ValidFromUtc");
                ttb.HasPeriodEnd("ValidToUtc");
                ttb.UseHistoryTable("AgroProfilesHistory");
            }));

            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.CropName).HasMaxLength(120).IsRequired();
            b.Property(x => x.GrowthStage).HasMaxLength(120).IsRequired();
            b.Property(x => x.ScientificNotes).HasMaxLength(800);
            b.Property(x => x.EmergencyPlaybookSteps).HasMaxLength(2500);
            b.Property(x => x.LastModifiedBy).HasMaxLength(150);
        });

        // 2. Климатические зоны ИрГАУ
        modelBuilder.Entity<ClimateZone>(b =>
        {
            b.ToTable("ClimateZones");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Name).HasMaxLength(180).IsRequired();
            b.Property(x => x.LocationCampus).HasMaxLength(160);
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.AreaUnit).HasMaxLength(20);
            b.Property(x => x.ControllerNodeId).HasMaxLength(64).IsRequired();
            b.Property(x => x.FirmwareVersion).HasMaxLength(64);
            b.Property(x => x.StatusNote).HasMaxLength(500);

            b.HasOne(x => x.AgroProfile)
                .WithMany(p => p.Zones)
                .HasForeignKey(x => x.AgroProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. Таблица временных рядов телеметрии (NONCLUSTERED COLUMNSTORE INDEX добавляется в миграции)
        modelBuilder.Entity<TelemetryReading>(b =>
        {
            b.ToTable("TelemetryReadings");
            b.HasKey(x => x.Id);
            b.Property(x => x.SensorCode).HasMaxLength(64).IsRequired();

            b.HasIndex(x => new { x.ZoneId, x.RecordedAtUtc })
                .HasDatabaseName("IX_TelemetryReadings_ZoneId_RecordedAtUtc");

            b.HasOne(x => x.Zone)
                .WithMany()
                .HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 4. Инциденты, чек-листы и хронология событий War Room
        modelBuilder.Entity<Incident>(b =>
        {
            b.ToTable("Incidents");
            b.HasKey(x => x.Id);
            b.Property(x => x.IncidentNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(x => x.IncidentNumber).IsUnique();
            b.HasIndex(x => new { x.ZoneId, x.Status });

            b.Property(x => x.Title).HasMaxLength(220).IsRequired();
            b.Property(x => x.AlertSummary).HasMaxLength(800);
            b.Property(x => x.AcknowledgedBy).HasMaxLength(160);
            b.Property(x => x.ResolvedBy).HasMaxLength(160);
            b.Property(x => x.RootCauseAnalysis).HasMaxLength(1200);
            b.Property(x => x.ResolutionSummary).HasMaxLength(1200);

            b.HasOne(x => x.Zone)
                .WithMany(z => z.Incidents)
                .HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentChecklistItem>(b =>
        {
            b.ToTable("IncidentChecklistItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Instruction).HasMaxLength(400).IsRequired();
            b.Property(x => x.CompletedBy).HasMaxLength(160);

            b.HasOne(x => x.Incident)
                .WithMany(i => i.ChecklistItems)
                .HasForeignKey(x => x.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentTimelineEvent>(b =>
        {
            b.ToTable("IncidentTimelineEvents");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.IncidentId, x.OccurredAtUtc });
            b.Property(x => x.AuthorName).HasMaxLength(120).IsRequired();
            b.Property(x => x.AuthorRole).HasMaxLength(120);
            b.Property(x => x.Message).HasMaxLength(1000).IsRequired();

            b.HasOne(x => x.Incident)
                .WithMany(i => i.TimelineEvents)
                .HasForeignKey(x => x.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 5. API-ключи аппаратных контроллеров ESP32 / Arduino
        modelBuilder.Entity<ApiKeyCredential>(b =>
        {
            b.ToTable("ApiKeyCredentials");
            b.HasKey(x => x.Id);
            b.Property(x => x.DeviceLabel).HasMaxLength(120).IsRequired();
            b.Property(x => x.KeyPrefix).HasMaxLength(24).IsRequired();
            b.Property(x => x.ApiKey).HasMaxLength(128).IsRequired();
            b.HasIndex(x => x.ApiKey).IsUnique();
            b.Property(x => x.CreatedBy).HasMaxLength(150);

            b.HasOne(x => x.Zone)
                .WithMany(z => z.ApiKeys)
                .HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
