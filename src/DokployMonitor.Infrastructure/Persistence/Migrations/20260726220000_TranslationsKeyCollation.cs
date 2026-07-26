using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Translation anahtarlari kaynak dilde buyuk/kucuk harfe duyarlidir (or. Error / ERROR).
/// SQL Server varsayilan collation CI oldugu icin tohumlama sirasinda EF ayni anahtari
/// izlemeye calisip patliyordu. Key kolonunu CS collation'a aliyoruz.
/// </summary>
[Migration(20260726220000, "Translations.Key: case-sensitive collation")]
public sealed class TranslationsKeyCollation : Migration
{
    public override void Up()
    {
        if (!Schema.Table("Translations").Exists())
        {
            return;
        }

        Delete.PrimaryKey("PK_Translations").FromTable("Translations");

        Execute.Sql(
            """
            ALTER TABLE [Translations]
            ALTER COLUMN [Key] nvarchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL;
            """);

        Create.PrimaryKey("PK_Translations")
            .OnTable("Translations")
            .Columns("Culture", "Key");
    }

    public override void Down()
    {
        if (!Schema.Table("Translations").Exists())
        {
            return;
        }

        Delete.PrimaryKey("PK_Translations").FromTable("Translations");

        Execute.Sql(
            """
            ALTER TABLE [Translations]
            ALTER COLUMN [Key] nvarchar(256) COLLATE database_default NOT NULL;
            """);

        Create.PrimaryKey("PK_Translations")
            .OnTable("Translations")
            .Columns("Culture", "Key");
    }
}
