using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Dokploy;
using DokployMonitor.Core.GitHub;
using DokployMonitor.Core.Localization;
using DokployMonitor.Core.Notifications;
using DokployMonitor.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Infrastructure.Persistence;

/// <summary>
/// Izleme verisi + panel kullanicilari (ASP.NET Core Identity) ayni SQL Server veritabaninda.
/// Sema FluentMigrator ile yonetilir; buradaki eslemeler yalnizca sorgu tarafi icindir.
/// </summary>
public sealed class MonitorDbContext(DbContextOptions<MonitorDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<DokployConnection> Connections => Set<DokployConnection>();
    public DbSet<GitHubAppRegistration> GitHubApps => Set<GitHubAppRegistration>();
    public DbSet<GitHubInstallation> GitHubInstallations => Set<GitHubInstallation>();
    public DbSet<GitHubRepoRules> GitHubRepoRules => Set<GitHubRepoRules>();
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<TrackedDeployment> Deployments => Set<TrackedDeployment>();
    public DbSet<DeploymentEvent> DeploymentEvents => Set<DeploymentEvent>();
    public DbSet<ErrorSignature> ErrorSignatures => Set<ErrorSignature>();
    public DbSet<WebhookNotification> WebhookNotifications => Set<WebhookNotification>();

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

        modelBuilder.Entity<GitHubAppRegistration>(entity =>
        {
            entity.ToTable("GitHubAppRegistrations");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasMaxLength(64);
            entity.Property(a => a.ClientId).HasMaxLength(128);
            entity.Property(a => a.Slug).HasMaxLength(128);
            entity.Property(a => a.Name).HasMaxLength(256);
            entity.Property(a => a.OwnerLogin).HasMaxLength(256);
        });

        modelBuilder.Entity<GitHubInstallation>(entity =>
        {
            entity.ToTable("GitHubInstallations");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasMaxLength(64);
            entity.Property(i => i.AppRegistrationId).HasMaxLength(64);
            entity.Property(i => i.AccountLogin).HasMaxLength(256);
            entity.Property(i => i.AccountType).HasMaxLength(32);
            entity.HasIndex(i => i.AppRegistrationId);
            entity.HasIndex(i => i.InstallationId).IsUnique();
            entity.HasOne<GitHubAppRegistration>()
                .WithMany()
                .HasForeignKey(i => i.AppRegistrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GitHubRepoRules>(entity =>
        {
            entity.ToTable("GitHubRepoRules");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasMaxLength(64);
            entity.Property(r => r.InstallationId).HasMaxLength(64);
            entity.Property(r => r.Owner).HasMaxLength(256);
            entity.Property(r => r.Repo).HasMaxLength(256);
            entity.HasIndex(r => new { r.InstallationId, r.Owner, r.Repo }).IsUnique();
            entity.HasOne<GitHubInstallation>()
                .WithMany()
                .HasForeignKey(r => r.InstallationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Translation>(entity =>
        {
            entity.ToTable("Translations");
            entity.HasKey(translation => new { translation.Culture, translation.Key });
            entity.Property(translation => translation.Culture).HasMaxLength(16);
            // Kaynak dil anahtarlari buyuk/kucuk harfe duyarlidir (Error vs ERROR).
            // SQL Server varsayilan collation CI oldugu icin Key CS collation ile tutulur.
            entity.Property(translation => translation.Key)
                .HasMaxLength(256)
                .UseCollation("Latin1_General_100_CS_AS");
            entity.HasIndex(translation => translation.Culture);
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
            // Indeksli kolonlar nvarchar(max) olamaz (SQL Server 900 bayt limiti).
            entity.Property(d => d.ServiceId).HasMaxLength(128);
            entity.Property(d => d.ProjectId).HasMaxLength(128);

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
}
