using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

[Migration(20260727160000, "GitHub App kayitlari ve kurulumlari")]
public sealed class GitHubSchema : Migration
{
    private const int AsMax = int.MaxValue;

    public override void Up()
    {
        if (Schema.Table("GitHubAppRegistrations").Exists())
        {
            return;
        }

        Create.Table("GitHubAppRegistrations")
            .WithColumn("Id").AsString(64).NotNullable().PrimaryKey("PK_GitHubAppRegistrations")
            .WithColumn("AppId").AsInt64().NotNullable()
            .WithColumn("ClientId").AsString(128).NotNullable()
            .WithColumn("ClientSecret").AsString(AsMax).NotNullable()
            .WithColumn("PrivateKeyPem").AsString(AsMax).NotNullable()
            .WithColumn("WebhookSecret").AsString(AsMax).Nullable()
            .WithColumn("Slug").AsString(128).NotNullable()
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("HtmlUrl").AsString(AsMax).Nullable()
            .WithColumn("OwnerLogin").AsString(256).Nullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable();

        Create.Table("GitHubInstallations")
            .WithColumn("Id").AsString(64).NotNullable().PrimaryKey("PK_GitHubInstallations")
            .WithColumn("AppRegistrationId").AsString(64).NotNullable()
                .ForeignKey("FK_GitHubInstallations_GitHubAppRegistrations_AppRegistrationId",
                    "GitHubAppRegistrations", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("InstallationId").AsInt64().NotNullable()
            .WithColumn("AccountLogin").AsString(256).NotNullable()
            .WithColumn("AccountType").AsString(32).NotNullable()
            .WithColumn("AccountAvatarUrl").AsString(AsMax).Nullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("LastSyncedAt").AsDateTimeOffset().Nullable();

        Create.Index("IX_GitHubInstallations_AppRegistrationId")
            .OnTable("GitHubInstallations")
            .OnColumn("AppRegistrationId");

        Create.Index("IX_GitHubInstallations_InstallationId")
            .OnTable("GitHubInstallations")
            .OnColumn("InstallationId")
            .Unique();
    }

    public override void Down()
    {
        if (Schema.Table("GitHubInstallations").Exists())
        {
            Delete.Table("GitHubInstallations");
        }

        if (Schema.Table("GitHubAppRegistrations").Exists())
        {
            Delete.Table("GitHubAppRegistrations");
        }
    }
}
