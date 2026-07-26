using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Birden fazla Dokploy sunucusu/API anahtari destegi: baglantilar veritabaninda tutulur
/// ve her deployment kaydi hangi baglantidan geldigini tasir.
///
/// Mevcut kayitlarin <c>ConnectionId</c> alani bos kalir; acilista ortam
/// degiskenlerinden ice aktarilan "varsayilan" baglanti ile eslestirilir.
/// </summary>
[Migration(20260726180000, "Coklu Dokploy baglantisi: DokployConnections + Deployments.ConnectionId")]
public sealed class MultipleConnections : Migration
{
    public override void Up()
    {
        if (!Schema.Table("DokployConnections").Exists())
        {
            Create.Table("DokployConnections")
                .WithColumn("Id").AsString(64).NotNullable().PrimaryKey("PK_DokployConnections")
                .WithColumn("Name").AsString(128).NotNullable()
                .WithColumn("BaseUrl").AsString().NotNullable()
                .WithColumn("ApiKey").AsString().NotNullable()
                .WithColumn("Enabled").AsBoolean().NotNullable()
                .WithColumn("AllowInvalidCertificates").AsBoolean().NotNullable()
                .WithColumn("ForceLegacyDiscovery").AsBoolean().NotNullable()
                .WithColumn("TimeoutSeconds").AsInt32().NotNullable()
                .WithColumn("MaxParallelRequests").AsInt32().NotNullable()
                .WithColumn("CreatedAt").AsString().NotNullable()
                .WithColumn("LastSyncAt").AsString().Nullable()
                .WithColumn("LastSyncError").AsString().Nullable();

            Create.Index("IX_DokployConnections_Name")
                .OnTable("DokployConnections").OnColumn("Name").Unique();
        }

        if (!Schema.Table("Deployments").Column("ConnectionId").Exists())
        {
            Alter.Table("Deployments").AddColumn("ConnectionId").AsString(64).Nullable();
            Create.Index("IX_Deployments_ConnectionId").OnTable("Deployments").OnColumn("ConnectionId");
        }
    }

    public override void Down()
    {
        Delete.Index("IX_Deployments_ConnectionId").OnTable("Deployments");
        Delete.Column("ConnectionId").FromTable("Deployments");
        Delete.Table("DokployConnections");
    }
}
