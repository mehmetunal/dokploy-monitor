using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ilk sema: deployment kayitlari, olay gecmisi, hata imzalari ve webhook bildirimleri.
///
/// Tum zaman kolonlari TEXT'tir: SQLite'in tarih tipi yok, zamanlari UTC ISO-8601 metin
/// olarak yaziyoruz (bkz. <see cref="MonitorDbContext"/> UtcIsoConverter). Bu format
/// hem sozluksel hem kronolojik siralanabildigi icin ORDER BY dogru calisir.
///
/// Kolon ve indeks adlari EF Core'un urettigi semayla birebir aynidir: sorgu katmani
/// hala EF Core oldugu icin isimlendirme sozlesmesinden sapmamak gerekiyor.
/// </summary>
[Migration(20260726093000, "Ilk sema: deployment, olay, hata imzasi ve webhook tablolari")]
public sealed class InitialSchema : Migration
{
    public override void Up()
    {
        // Sema EF Core migration'lari ile olusturulmus bir veritabaninda zaten var olabilir.
        // Bu durumda tablolari yeniden yaratmayi denemeyip kaydi FluentMigrator'a devrediyoruz;
        // runner bu migration'i uygulanmis olarak isaretler (VersionInfo).
        if (Schema.Table("Deployments").Exists())
        {
            foreach (var leftover in new[] { "__EFMigrationsHistory", "__EFMigrationsLock" })
            {
                if (Schema.Table(leftover).Exists())
                {
                    Delete.Table(leftover);
                }
            }

            return;
        }

        Create.Table("Deployments")
            .WithColumn("DeploymentId").AsString(64).NotNullable().PrimaryKey("PK_Deployments")
            .WithColumn("Status").AsString(16).NotNullable()
            .WithColumn("Title").AsString().Nullable()
            .WithColumn("Description").AsString().Nullable()
            .WithColumn("ErrorMessage").AsString().Nullable()
            .WithColumn("LogPath").AsString().Nullable()
            .WithColumn("Pid").AsString().Nullable()
            .WithColumn("ApplicationId").AsString().Nullable()
            .WithColumn("ComposeId").AsString().Nullable()
            .WithColumn("ServerId").AsString().Nullable()
            .WithColumn("ScheduleId").AsString().Nullable()
            .WithColumn("BackupId").AsString().Nullable()
            .WithColumn("VolumeBackupId").AsString().Nullable()
            .WithColumn("PreviewDeploymentId").AsString().Nullable()
            .WithColumn("IsPreviewDeployment").AsBoolean().NotNullable()
            .WithColumn("ServiceType").AsString(32).NotNullable()
            .WithColumn("ServiceId").AsString().Nullable()
            .WithColumn("ServiceName").AsString().Nullable()
            .WithColumn("AppName").AsString().Nullable()
            .WithColumn("ProjectId").AsString().Nullable()
            .WithColumn("ProjectName").AsString().Nullable()
            .WithColumn("EnvironmentId").AsString().Nullable()
            .WithColumn("EnvironmentName").AsString().Nullable()
            .WithColumn("ServerName").AsString().Nullable()
            .WithColumn("BuildServerName").AsString().Nullable()
            .WithColumn("CreatedAt").AsString().NotNullable()
            .WithColumn("StartedAt").AsString().Nullable()
            .WithColumn("FinishedAt").AsString().Nullable()
            .WithColumn("DurationSeconds").AsInt32().Nullable()
            .WithColumn("ErrorSignatureHash").AsString(32).Nullable()
            .WithColumn("FirstSeenAt").AsString().NotNullable()
            .WithColumn("LastUpdatedAt").AsString().NotNullable()
            .WithColumn("ArchivedLogPath").AsString().Nullable()
            .WithColumn("RawJson").AsString().Nullable();

        Create.Table("ErrorSignatures")
            .WithColumn("Hash").AsString(32).NotNullable().PrimaryKey("PK_ErrorSignatures")
            .WithColumn("NormalizedMessage").AsString().NotNullable()
            .WithColumn("SampleMessage").AsString().NotNullable()
            .WithColumn("OccurrenceCount").AsInt32().NotNullable()
            .WithColumn("FirstSeenAt").AsString().NotNullable()
            .WithColumn("LastSeenAt").AsString().NotNullable()
            .WithColumn("LastServiceName").AsString().Nullable();

        Create.Table("WebhookNotifications")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey("PK_WebhookNotifications").Identity()
            .WithColumn("ReceivedAt").AsString().NotNullable()
            .WithColumn("OccurredAt").AsString().Nullable()
            .WithColumn("Title").AsString().Nullable()
            .WithColumn("Message").AsString().Nullable()
            .WithColumn("Status").AsString(32).Nullable()
            .WithColumn("Type").AsString(32).Nullable()
            .WithColumn("ProjectName").AsString().Nullable()
            .WithColumn("ApplicationName").AsString().Nullable()
            .WithColumn("ApplicationType").AsString().Nullable()
            .WithColumn("ErrorMessage").AsString().Nullable()
            .WithColumn("BuildLink").AsString().Nullable()
            .WithColumn("Domains").AsString().Nullable()
            .WithColumn("ServiceId").AsString().Nullable()
            .WithColumn("ProjectId").AsString().Nullable()
            .WithColumn("RawJson").AsString().Nullable();

        // FK satir ici tanimlanmali: SQLite ALTER TABLE ADD CONSTRAINT desteklemiyor.
        // Cascade sart: RetentionWorker deployment'lari ExecuteDelete ile (EF change tracker'i
        // devre disi kalacak sekilde) siliyor, olay kayitlarini veritabani temizliyor.
        Create.Table("DeploymentEvents")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey("PK_DeploymentEvents").Identity()
            .WithColumn("DeploymentId").AsString(64).NotNullable()
                .ForeignKey("FK_DeploymentEvents_Deployments_DeploymentId", "Deployments", "DeploymentId")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("EventType").AsString(24).NotNullable()
            .WithColumn("Source").AsString(16).NotNullable()
            .WithColumn("FromStatus").AsString(16).Nullable()
            .WithColumn("ToStatus").AsString(16).Nullable()
            .WithColumn("Message").AsString().Nullable()
            .WithColumn("OccurredAt").AsString().NotNullable();

        Create.Index("IX_Deployments_CreatedAt").OnTable("Deployments").OnColumn("CreatedAt");
        Create.Index("IX_Deployments_Status").OnTable("Deployments").OnColumn("Status");
        Create.Index("IX_Deployments_ServiceId").OnTable("Deployments").OnColumn("ServiceId");
        Create.Index("IX_Deployments_ProjectId").OnTable("Deployments").OnColumn("ProjectId");
        Create.Index("IX_Deployments_ErrorSignatureHash").OnTable("Deployments").OnColumn("ErrorSignatureHash");

        Create.Index("IX_ErrorSignatures_LastSeenAt").OnTable("ErrorSignatures").OnColumn("LastSeenAt");

        Create.Index("IX_WebhookNotifications_ReceivedAt").OnTable("WebhookNotifications").OnColumn("ReceivedAt");

        Create.Index("IX_DeploymentEvents_DeploymentId").OnTable("DeploymentEvents").OnColumn("DeploymentId");
        Create.Index("IX_DeploymentEvents_OccurredAt").OnTable("DeploymentEvents").OnColumn("OccurredAt");
    }

    public override void Down()
    {
        Delete.Table("DeploymentEvents");
        Delete.Table("WebhookNotifications");
        Delete.Table("ErrorSignatures");
        Delete.Table("Deployments");
    }
}
