using System.Globalization;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Dokploy;
using DokployMonitor.Core.Notifications;
using DokployMonitor.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DokployMonitor.Infrastructure.Persistence;

/// <summary>
/// Izleme verisi + panel kullanicilari (ASP.NET Core Identity) ayni SQLite dosyasinda.
/// Sema FluentMigrator ile yonetilir; buradaki eslemeler yalnizca sorgu tarafi icindir.
/// </summary>
public sealed class MonitorDbContext(DbContextOptions<MonitorDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<DokployConnection> Connections => Set<DokployConnection>();
    public DbSet<TrackedDeployment> Deployments => Set<TrackedDeployment>();
    public DbSet<DeploymentEvent> DeploymentEvents => Set<DeploymentEvent>();
    public DbSet<ErrorSignature> ErrorSignatures => Set<ErrorSignature>();
    public DbSet<WebhookNotification> WebhookNotifications => Set<WebhookNotification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite'in native tarih tipi yok. Tum zamanlari UTC ISO-8601 metin olarak yaziyoruz:
        // bu format hem sozluksel hem kronolojik olarak siralanabilir, dolayisiyla
        // ORDER BY / karsilastirma sorgulari dogru calisir.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcIsoConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity tablolarinin (AspNetUsers, AspNetRoles, ...) eslemesi.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DokployConnection>(entity =>
        {
            entity.ToTable("DokployConnections");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasMaxLength(64);
            entity.Property(c => c.Name).HasMaxLength(128);
            entity.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<TrackedDeployment>(entity =>
        {
            entity.HasKey(d => d.DeploymentId);
            entity.Property(d => d.DeploymentId).HasMaxLength(64);
            entity.Property(d => d.ConnectionId).HasMaxLength(64);
            entity.HasIndex(d => d.ConnectionId);
            entity.Property(d => d.ServiceType).HasMaxLength(32);
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(d => d.ErrorSignatureHash).HasMaxLength(32);

            entity.HasIndex(d => d.CreatedAt);
            entity.HasIndex(d => d.Status);
            entity.HasIndex(d => d.ServiceId);
            entity.HasIndex(d => d.ProjectId);
            entity.HasIndex(d => d.ErrorSignatureHash);
        });

        modelBuilder.Entity<DeploymentEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeploymentId).HasMaxLength(64);
            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(24);
            entity.Property(e => e.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(16);

            entity.HasIndex(e => e.OccurredAt);
            entity.HasOne(e => e.Deployment)
                .WithMany()
                .HasForeignKey(e => e.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ErrorSignature>(entity =>
        {
            entity.HasKey(s => s.Hash);
            entity.Property(s => s.Hash).HasMaxLength(32);
            entity.HasIndex(s => s.LastSeenAt);
        });

        modelBuilder.Entity<WebhookNotification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Status).HasMaxLength(32);
            entity.Property(n => n.Type).HasMaxLength(32);
            entity.HasIndex(n => n.ReceivedAt);
        });
    }

    private sealed class UtcIsoConverter() : ValueConverter<DateTimeOffset, string>(
        value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
        text => DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
    }
}
