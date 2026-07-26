using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ilk sema: deployment kayitlari, olay gecmisi, hata imzalari ve webhook bildirimleri.
///
/// Zaman kolonlari <c>datetimeoffset</c> tipindedir (SQL Server native).
/// Sinirsiz metin alanlari <c>nvarchar(max)</c> olarak tanimlanir (<see cref="AsMax"/>).
///
/// Kolon ve indeks adlari EF Core'un urettigi semayla birebir aynidir: sorgu katmani
/// hala EF Core oldugu icin isimlendirme sozlesmesinden sapmamak gerekiyor.
/// </summary>
[Migration(20260726093000, "Ilk sema: deployment, olay, hata imzasi ve webhook tablolari")]
public sealed class InitialSchema : Migration
{
    /// <summary>FluentMigrator'da nvarchar(max) karsiligi.</summary>
    private const int AsMax = int.MaxValue;

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
            .WithColumn("Title").AsString(AsMax).Nullable()
            .WithColumn("Description").AsString(AsMax).Nullable()
            .WithColumn("ErrorMessage").AsString(AsMax).Nullable()
            .WithColumn("LogPath").AsString(AsMax).Nullable()
            .WithColumn("Pid").AsString(AsMax).Nullable()
            .WithColumn("ApplicationId").AsString(AsMax).Nullable()
            .WithColumn("ComposeId").AsString(AsMax).Nullable()
            .WithColumn("ServerId").AsString(AsMax).Nullable()
            .WithColumn("ScheduleId").AsString(AsMax).Nullable()
            .WithColumn("BackupId").AsString(AsMax).Nullable()
            .WithColumn("VolumeBackupId").AsString(AsMax).Nullable()
            .WithColumn("PreviewDeploymentId").AsString(AsMax).Nullable()
            .WithColumn("IsPreviewDeployment").AsBoolean().NotNullable()
            .WithColumn("ServiceType").AsString(32).NotNullable()
            .WithColumn("ServiceId").AsString(128).Nullable()
            .WithColumn("ServiceName").AsString(AsMax).Nullable()
            .WithColumn("AppName").AsString(AsMax).Nullable()
            .WithColumn("ProjectId").AsString(128).Nullable()
            .WithColumn("ProjectName").AsString(AsMax).Nullable()
            .WithColumn("EnvironmentId").AsString(AsMax).Nullable()
            .WithColumn("EnvironmentName").AsString(AsMax).Nullable()
            .WithColumn("ServerName").AsString(AsMax).Nullable()
            .WithColumn("BuildServerName").AsString(AsMax).Nullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("StartedAt").AsDateTimeOffset().Nullable()
            .WithColumn("FinishedAt").AsDateTimeOffset().Nullable()
            .WithColumn("DurationSeconds").AsInt32().Nullable()
            .WithColumn("ErrorSignatureHash").AsString(32).Nullable()
            .WithColumn("FirstSeenAt").AsDateTimeOffset().NotNullable()
            .WithColumn("LastUpdatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("ArchivedLogPath").AsString(AsMax).Nullable()
            .WithColumn("RawJson").AsString(AsMax).Nullable();

        Create.Table("ErrorSignatures")
            .WithColumn("Hash").AsString(32).NotNullable().PrimaryKey("PK_ErrorSignatures")
            .WithColumn("NormalizedMessage").AsString(AsMax).NotNullable()
            .WithColumn("SampleMessage").AsString(AsMax).NotNullable()
            .WithColumn("OccurrenceCount").AsInt32().NotNullable()
            .WithColumn("FirstSeenAt").AsDateTimeOffset().NotNullable()
            .WithColumn("LastSeenAt").AsDateTimeOffset().NotNullable()
            .WithColumn("LastServiceName").AsString(AsMax).Nullable();

        Create.Table("WebhookNotifications")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey("PK_WebhookNotifications").Identity()
            .WithColumn("ReceivedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("OccurredAt").AsDateTimeOffset().Nullable()
            .WithColumn("Title").AsString(AsMax).Nullable()
            .WithColumn("Message").AsString(AsMax).Nullable()
            .WithColumn("Status").AsString(32).Nullable()
            .WithColumn("Type").AsString(32).Nullable()
            .WithColumn("ProjectName").AsString(AsMax).Nullable()
            .WithColumn("ApplicationName").AsString(AsMax).Nullable()
            .WithColumn("ApplicationType").AsString(AsMax).Nullable()
            .WithColumn("ErrorMessage").AsString(AsMax).Nullable()
            .WithColumn("BuildLink").AsString(AsMax).Nullable()
            .WithColumn("Domains").AsString(AsMax).Nullable()
            .WithColumn("ServiceId").AsString(AsMax).Nullable()
            .WithColumn("ProjectId").AsString(AsMax).Nullable()
            .WithColumn("RawJson").AsString(AsMax).Nullable();

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
            .WithColumn("Message").AsString(AsMax).Nullable()
            .WithColumn("OccurredAt").AsDateTimeOffset().NotNullable();

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
