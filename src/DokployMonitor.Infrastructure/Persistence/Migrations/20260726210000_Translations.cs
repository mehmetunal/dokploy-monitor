using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Arayuz cevirileri veritabaninda tutulur (resx dosyasi yok): SuperAdmin panelden
/// duzenleyebilir, yeni dil eklemek icin yeniden derleme gerekmez.
///
/// Birincil anahtar (Culture, Key): ayni dil icin bir anahtar yalnizca bir kez.
/// </summary>
[Migration(20260726210000, "Arayuz cevirileri tablosu")]
public sealed class Translations : Migration
{
    private const int AsMax = int.MaxValue;

    public override void Up()
    {
        if (Schema.Table("Translations").Exists())
        {
            return;
        }

        Create.Table("Translations")
            .WithColumn("Culture").AsString(16).NotNullable().PrimaryKey("PK_Translations")
            .WithColumn("Key").AsString(256).NotNullable().PrimaryKey("PK_Translations")
            .WithColumn("Value").AsString(AsMax).Nullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UpdatedBy").AsString(AsMax).Nullable();

        // Dil basina tum satirlar tek seferde okunuyor (localizer anlik goruntusu).
        Create.Index("IX_Translations_Culture").OnTable("Translations").OnColumn("Culture");
    }

    public override void Down() => Delete.Table("Translations");
}
