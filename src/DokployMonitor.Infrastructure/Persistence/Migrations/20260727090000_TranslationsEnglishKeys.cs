using FluentMigrator;

namespace DokployMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Translation keys switched from Turkish source texts to **English**.
///
/// Old rows are keyed by the previous Turkish texts and can no longer be matched, so the
/// table is cleared and re-seeded on startup with English keys (Turkish becomes a regular
/// translation). One-time data loss is limited to translations edited in the admin UI
/// before this change.
/// </summary>
[Migration(20260727090000, "Translations: switch keys to English source texts")]
public sealed class TranslationsEnglishKeys : Migration
{
    public override void Up() => Delete.FromTable("Translations").AllRows();

    public override void Down()
    {
        // Nothing to restore: rows are re-created by the seeder.
    }
}
