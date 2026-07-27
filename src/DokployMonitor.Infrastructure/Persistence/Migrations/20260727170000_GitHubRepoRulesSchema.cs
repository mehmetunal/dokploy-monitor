using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

[Migration(20260727170000, "GitHub repo branch kurallari")]
public sealed class GitHubRepoRulesSchema : Migration
{
    private const int AsMax = int.MaxValue;

    public override void Up()
    {
        if (Schema.Table("GitHubRepoRules").Exists())
        {
            return;
        }

        Create.Table("GitHubRepoRules")
            .WithColumn("Id").AsString(64).NotNullable().PrimaryKey("PK_GitHubRepoRules")
            .WithColumn("InstallationId").AsString(64).NotNullable()
                .ForeignKey("FK_GitHubRepoRules_GitHubInstallations_InstallationId",
                    "GitHubInstallations", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Owner").AsString(256).NotNullable()
            .WithColumn("Repo").AsString(256).NotNullable()
            .WithColumn("AllowCreateBranch").AsBoolean().NotNullable()
            .WithColumn("AllowMergeBranches").AsBoolean().NotNullable()
            .WithColumn("AllowDeleteBranch").AsBoolean().NotNullable()
            .WithColumn("AllowedCreateFromBranches").AsString(AsMax).NotNullable()
            .WithColumn("AllowedMergeIntoBranches").AsString(AsMax).NotNullable()
            .WithColumn("ForbiddenMergeIntoBranches").AsString(AsMax).NotNullable()
            .WithColumn("ProtectedFromDeleteBranches").AsString(AsMax).NotNullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable();

        Create.Index("IX_GitHubRepoRules_Installation_Owner_Repo")
            .OnTable("GitHubRepoRules")
            .OnColumn("InstallationId").Ascending()
            .OnColumn("Owner").Ascending()
            .OnColumn("Repo").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        if (Schema.Table("GitHubRepoRules").Exists())
        {
            Delete.Table("GitHubRepoRules");
        }
    }
}
