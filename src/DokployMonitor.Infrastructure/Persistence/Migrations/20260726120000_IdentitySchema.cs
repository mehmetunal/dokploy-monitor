using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// ASP.NET Core Identity tablolari. Kolon adlari ve tipleri EF Core'un Identity
/// eslemesiyle birebir ayni olmali; sapma <c>MigrationSchemaTests</c> ile yakalanir.
/// </summary>
[Migration(20260726120000, "Identity: kullanici, rol ve talep tablolari")]
public sealed class IdentitySchema : Migration
{
    private const int AsMax = int.MaxValue;
    private const int IdentityKeyLength = 450;

    public override void Up()
    {
        // Sema EF Core tarafindan olusturulmus bir veritabaninda tablolar zaten var olabilir
        // (bkz. InitialSchema'daki ayni devralma korumasi).
        if (Schema.Table("AspNetUsers").Exists())
        {
            return;
        }

        Create.Table("AspNetRoles")
            .WithColumn("Id").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetRoles")
            .WithColumn("Name").AsString(256).Nullable()
            .WithColumn("NormalizedName").AsString(256).Nullable()
            .WithColumn("ConcurrencyStamp").AsString(AsMax).Nullable();

        Create.Table("AspNetUsers")
            .WithColumn("Id").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUsers")
            .WithColumn("DisplayName").AsString(AsMax).Nullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UserName").AsString(256).Nullable()
            .WithColumn("NormalizedUserName").AsString(256).Nullable()
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("NormalizedEmail").AsString(256).Nullable()
            .WithColumn("EmailConfirmed").AsBoolean().NotNullable()
            .WithColumn("PasswordHash").AsString(AsMax).Nullable()
            .WithColumn("SecurityStamp").AsString(AsMax).Nullable()
            .WithColumn("ConcurrencyStamp").AsString(AsMax).Nullable()
            .WithColumn("PhoneNumber").AsString(AsMax).Nullable()
            .WithColumn("PhoneNumberConfirmed").AsBoolean().NotNullable()
            .WithColumn("TwoFactorEnabled").AsBoolean().NotNullable()
            .WithColumn("LockoutEnd").AsDateTimeOffset().Nullable()
            .WithColumn("LockoutEnabled").AsBoolean().NotNullable()
            .WithColumn("AccessFailedCount").AsInt32().NotNullable();

        Create.Table("AspNetRoleClaims")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey("PK_AspNetRoleClaims").Identity()
            .WithColumn("RoleId").AsString(IdentityKeyLength).NotNullable()
                .ForeignKey("FK_AspNetRoleClaims_AspNetRoles_RoleId", "AspNetRoles", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ClaimType").AsString(AsMax).Nullable()
            .WithColumn("ClaimValue").AsString(AsMax).Nullable();

        Create.Table("AspNetUserClaims")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey("PK_AspNetUserClaims").Identity()
            .WithColumn("UserId").AsString(IdentityKeyLength).NotNullable()
                .ForeignKey("FK_AspNetUserClaims_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ClaimType").AsString(AsMax).Nullable()
            .WithColumn("ClaimValue").AsString(AsMax).Nullable();

        Create.Table("AspNetUserLogins")
            .WithColumn("LoginProvider").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserLogins")
            .WithColumn("ProviderKey").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserLogins")
            .WithColumn("ProviderDisplayName").AsString(AsMax).Nullable()
            .WithColumn("UserId").AsString(IdentityKeyLength).NotNullable()
                .ForeignKey("FK_AspNetUserLogins_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade);

        Create.Table("AspNetUserRoles")
            .WithColumn("UserId").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserRoles")
                .ForeignKey("FK_AspNetUserRoles_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("RoleId").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserRoles")
                .ForeignKey("FK_AspNetUserRoles_AspNetRoles_RoleId", "AspNetRoles", "Id")
                .OnDelete(System.Data.Rule.Cascade);

        Create.Table("AspNetUserTokens")
            .WithColumn("UserId").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserTokens")
                .ForeignKey("FK_AspNetUserTokens_AspNetUsers_UserId", "AspNetUsers", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("LoginProvider").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserTokens")
            .WithColumn("Name").AsString(IdentityKeyLength).NotNullable().PrimaryKey("PK_AspNetUserTokens")
            .WithColumn("Value").AsString(AsMax).Nullable();

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
