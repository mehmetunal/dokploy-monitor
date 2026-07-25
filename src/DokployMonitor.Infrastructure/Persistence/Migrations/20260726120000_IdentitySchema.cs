using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// ASP.NET Core Identity tablolari. Kolon adlari ve tipleri EF Core'un Identity
/// eslemesiyle birebir ayni olmali; sapma <c>MigrationSchemaTests</c> ile yakalanir.
///
/// Tarih kolonlari (LockoutEnd, CreatedAt) TEXT'tir: DateTimeOffset degerleri UTC
/// ISO-8601 metin olarak yazilir (bkz. MonitorDbContext.UtcIsoConverter).
/// </summary>
[Migration(20260726120000, "Identity: kullanici, rol ve talep tablolari")]
public sealed class IdentitySchema : Migration
{
    public override void Up()
    {
        // Sema EF Core tarafindan olusturulmus bir veritabaninda tablolar zaten var olabilir
        // (bkz. InitialSchema'daki ayni devralma korumasi).
        if (Schema.Table("AspNetUsers").Exists())
        {
            return;
        }

        Create.Table("AspNetRoles")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey("PK_AspNetRoles")
            .WithColumn("Name").AsString(256).Nullable()
            .WithColumn("NormalizedName").AsString(256).Nullable()
            .WithColumn("ConcurrencyStamp").AsString().Nullable();

        Create.Table("AspNetUsers")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey("PK_AspNetUsers")
            .WithColumn("DisplayName").AsString().Nullable()
            .WithColumn("CreatedAt").AsString().NotNullable()
            .WithColumn("UserName").AsString(256).Nullable()
            .WithColumn("NormalizedUserName").AsString(256).Nullable()
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("NormalizedEmail").AsString(256).Nullable()
            .WithColumn("EmailConfirmed").AsBoolean().NotNullable()
            .WithColumn("PasswordHash").AsString().Nullable()
            .WithColumn("SecurityStamp").AsString().Nullable()
            .WithColumn("ConcurrencyStamp").AsString().Nullable()
            .WithColumn("PhoneNumber").AsString().Nullable()
            .WithColumn("PhoneNumberConfirmed").AsBoolean().NotNullable()
            .WithColumn("TwoFactorEnabled").AsBoolean().NotNullable()
            .WithColumn("LockoutEnd").AsString().Nullable()
            .WithColumn("LockoutEnabled").AsBoolean().NotNullable()
            .WithColumn("AccessFailedCount").AsInt32().NotNullable();

        Create.Table("AspNetRoleClaims")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey("PK_AspNetRoleClaims").Identity()
            .WithColumn("RoleId").AsString().NotNullable()
                .ForeignKey("FK_AspNetRoleClaims_AspNetRoles_RoleId", "AspNetRoles", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ClaimType").AsString().Nullable()
            .WithColumn("ClaimValue").AsString().Nullable();

        Create.Table("AspNetUserClaims")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey("PK_AspNetUserClaims").Identity()
            .WithColumn("UserId").AsString().NotNullable()
                .ForeignKey("FK_AspNetUserClaims_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ClaimType").AsString().Nullable()
            .WithColumn("ClaimValue").AsString().Nullable();

        Create.Table("AspNetUserLogins")
            .WithColumn("LoginProvider").AsString().NotNullable().PrimaryKey("PK_AspNetUserLogins")
            .WithColumn("ProviderKey").AsString().NotNullable().PrimaryKey("PK_AspNetUserLogins")
            .WithColumn("ProviderDisplayName").AsString().Nullable()
            .WithColumn("UserId").AsString().NotNullable()
                .ForeignKey("FK_AspNetUserLogins_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade);

        Create.Table("AspNetUserRoles")
            .WithColumn("UserId").AsString().NotNullable().PrimaryKey("PK_AspNetUserRoles")
                .ForeignKey("FK_AspNetUserRoles_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("RoleId").AsString().NotNullable().PrimaryKey("PK_AspNetUserRoles")
                .ForeignKey("FK_AspNetUserRoles_AspNetRoles_RoleId", "AspNetRoles", "Id")
                .OnDelete(System.Data.Rule.Cascade);

        Create.Table("AspNetUserTokens")
            .WithColumn("UserId").AsString().NotNullable().PrimaryKey("PK_AspNetUserTokens")
                .ForeignKey("FK_AspNetUserTokens_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("LoginProvider").AsString().NotNullable().PrimaryKey("PK_AspNetUserTokens")
            .WithColumn("Name").AsString().NotNullable().PrimaryKey("PK_AspNetUserTokens")
            .WithColumn("Value").AsString().Nullable();

        // Identity, normalize edilmis ad/e-posta uzerinden arama yapar; adlar EF ile ayni.
        Create.Index("RoleNameIndex").OnTable("AspNetRoles").OnColumn("NormalizedName").Unique();
        Create.Index("UserNameIndex").OnTable("AspNetUsers").OnColumn("NormalizedUserName").Unique();
        Create.Index("EmailIndex").OnTable("AspNetUsers").OnColumn("NormalizedEmail");

        Create.Index("IX_AspNetRoleClaims_RoleId").OnTable("AspNetRoleClaims").OnColumn("RoleId");
        Create.Index("IX_AspNetUserClaims_UserId").OnTable("AspNetUserClaims").OnColumn("UserId");
        Create.Index("IX_AspNetUserLogins_UserId").OnTable("AspNetUserLogins").OnColumn("UserId");
        Create.Index("IX_AspNetUserRoles_RoleId").OnTable("AspNetUserRoles").OnColumn("RoleId");
    }

    public override void Down()
    {
        Delete.Table("AspNetUserTokens");
        Delete.Table("AspNetUserRoles");
        Delete.Table("AspNetUserLogins");
        Delete.Table("AspNetUserClaims");
        Delete.Table("AspNetRoleClaims");
        Delete.Table("AspNetUsers");
        Delete.Table("AspNetRoles");
    }
}
