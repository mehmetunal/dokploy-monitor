using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Varsayilan kimlik bilgileriyle olusturulan hesabin ilk girisinde e-posta ve
/// parola degistirmeye zorlanmasi icin bayrak.
/// </summary>
[Migration(20260726150000, "AspNetUsers: MustChangeCredentials bayragi")]
public sealed class UserMustChangeCredentials : Migration
{
    public override void Up()
    {
        if (Schema.Table("AspNetUsers").Column("MustChangeCredentials").Exists())
        {
            return;
        }

        // Mevcut hesaplar etkilenmesin: varsayilan false.
        Alter.Table("AspNetUsers")
            .AddColumn("MustChangeCredentials").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down() =>
        Delete.Column("MustChangeCredentials").FromTable("AspNetUsers");
}
