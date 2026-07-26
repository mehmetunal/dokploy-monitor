using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Translation anahtarlari kaynak dilde buyuk/kucuk harfe duyarlidir (or. Error / ERROR).
/// SQL Server varsayilan collation CI oldugu icin Key kolonunu CS collation'a aliyoruz.
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

        // FluentMigrator PK API'si bazi SQL Server surumlerinde takilabiliyor; ham SQL daha guvenilir.
        Execute.Sql(
            """
            DECLARE @pk sysname =
                (SELECT kc.name
                 FROM sys.key_constraints kc
                 WHERE kc.parent_object_id = OBJECT_ID(N'dbo.Translations')
                   AND kc.type = 'PK');

            IF @pk IS NOT NULL
                EXEC(N'ALTER TABLE [Translations] DROP CONSTRAINT [' + @pk + N']');

            ALTER TABLE [Translations]
            ALTER COLUMN [Key] nvarchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL;

            ALTER TABLE [Translations]
            ADD CONSTRAINT [PK_Translations] PRIMARY KEY ([Culture], [Key]);
            """);
    }

    public override void Down()
    {
        if (!Schema.Table("Translations").Exists())
        {
            return;
        }

        Execute.Sql(
            """
            DECLARE @pk sysname =
                (SELECT kc.name
                 FROM sys.key_constraints kc
                 WHERE kc.parent_object_id = OBJECT_ID(N'dbo.Translations')
                   AND kc.type = 'PK');

            IF @pk IS NOT NULL
                EXEC(N'ALTER TABLE [Translations] DROP CONSTRAINT [' + @pk + N']');

            ALTER TABLE [Translations]
            ALTER COLUMN [Key] nvarchar(256) COLLATE database_default NOT NULL;

            ALTER TABLE [Translations]
            ADD CONSTRAINT [PK_Translations] PRIMARY KEY ([Culture], [Key]);
            """);
    }
}
