using System.Collections.Concurrent;
using DokployMonitor.Core.Localization;
using DokployMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DokployMonitor.Infrastructure.Localization;

/// <summary>
/// Cevirilerin bellek ici anlik goruntusu.
///
/// <c>IStringLocalizer</c> indeksleyicisi **senkron** oldugu icin her metin icin
/// veritabanina gidilemez: tum satirlar acilista yuklenir, panelden bir duzenleme
/// yapilinca aninda, ayrica arka planda periyodik olarak tazelenir (coklu ornek
/// kurulumunda diger ornekler bu periyotta yakalar).
///
/// Bulunamayan anahtarlar biriktirilir ve tazeleme sirasinda veritabanina "bos ceviri"
/// olarak yazilir; boylece SuperAdmin ekranda hangi metinlerin cevrilmedigini gorur.
/// </summary>
public sealed class TranslationStore(
    IDbContextFactory<MonitorDbContext> dbFactory,
    ILogger<TranslationStore> logger)
{
    private volatile Dictionary<string, Dictionary<string, string>> _snapshot =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<(string Culture, string Key), byte> _missing = new();

    public DateTimeOffset? LoadedAt { get; private set; }

    public int CultureCount => _snapshot.Count;

    /// <summary>Ceviriyi doner; yoksa null (cagiran taraf anahtari gosterir).</summary>
    public string? Find(string culture, string key)
    {
        if (_snapshot.TryGetValue(culture, out var translations)
            && translations.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // Eksik anahtari not al: bir sonraki tazelemede veritabanina yazilir.
        _missing.TryAdd((culture, key), 0);
        return null;
    }

    /// <summary>Bir dilin tum cevirileri (yonetim ekrani ve listeleme icin).</summary>
    public IReadOnlyDictionary<string, string> All(string culture) =>
        _snapshot.TryGetValue(culture, out var translations)
            ? translations
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Veritabanindan tam yukleme yapar ve eksik anahtarlari kaydeder.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            await PersistMissingKeysAsync(db, ct);

            var rows = await db.Translations.AsNoTracking().ToListAsync(ct);

            var snapshot = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (!snapshot.TryGetValue(row.Culture, out var translations))
                {
                    translations = new Dictionary<string, string>(StringComparer.Ordinal);
                    snapshot[row.Culture] = translations;
                }

                if (!string.IsNullOrWhiteSpace(row.Value))
                {
                    translations[row.Key] = row.Value!;
                }
            }

            _snapshot = snapshot;
            LoadedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ceviri yuklenemezse uygulama kaynak dille (Turkce) calismaya devam eder.
            logger.LogError(ex, "Ceviriler yuklenemedi; kaynak dil metinleri kullanilacak.");
        }
    }

    /// <summary>
    /// Ekranlarda gorulen ama veritabaninda olmayan anahtarlari bos ceviri olarak ekler.
    /// Boylece yonetim ekraninda "cevrilmemis" listesinde gorunurler.
    /// </summary>
    private async Task PersistMissingKeysAsync(MonitorDbContext db, CancellationToken ct)
    {
        if (_missing.IsEmpty)
        {
            return;
        }

        var pending = _missing.Keys.ToList();
        _missing.Clear();

        var added = 0;

        foreach (var (culture, key) in pending)
        {
            var exists = await db.Translations
                .AnyAsync(row => row.Culture == culture && row.Key == key, ct);

            if (exists)
            {
                continue;
            }

            db.Translations.Add(new Translation
            {
                Culture = culture,
                Key = key,
                Value = null,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("{Count} cevrilmemis anahtar kaydedildi.", added);
        }
    }

    /// <summary>
    /// Tohum cevirileri uygular:
    ///  · satir yoksa eklenir,
    ///  · satir var ama **degeri bossa** (otomatik toplanmis "cevrilmemis" kayit) doldurulur,
    ///  · dolu degerlere **asla dokunulmaz** — panelden yapilan duzenlemeler korunur.
    ///
    /// Not: EF Core SQL Server change tracker, kolon CS olsa bile ayni context icinde
    /// <c>Error</c>/<c>ERROR</c> ciftini cakistirabiliyor. Bu yuzden tohumlama
    /// case-insensitive tekil anahtar uzerinden ilerler; buyuk/kucuk harf varyantlari
    /// ayri bir turda (yeni DbContext) eklenir.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var existing = await db.Translations.ToListAsync(ct);
            // IgnoreCase: ayni context'te Error/ERROR ciftini iki kez Add etme.
            var index = new Dictionary<(string Culture, string Key), Translation>(
                StringTupleComparer.OrdinalIgnoreCase);

            foreach (var row in existing)
            {
                index.TryAdd((row.Culture, row.Key), row);
            }

            var added = 0;
            var filled = 0;

            foreach (var (culture, entries) in TranslationDefaults.Seed)
            {
                foreach (var (key, value) in entries)
                {
                    if (index.TryGetValue((culture, key), out var row))
                    {
                        if (!row.IsTranslated && !string.IsNullOrWhiteSpace(value))
                        {
                            row.Value = value;
                            row.UpdatedAt = DateTimeOffset.UtcNow;
                            filled++;
                        }

                        continue;
                    }

                    var entity = new Translation
                    {
                        Culture = culture,
                        Key = key,
                        Value = value,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    };

                    db.Translations.Add(entity);
                    index[(culture, key)] = entity;
                    added++;
                }
            }

            if (added > 0 || filled > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Ceviri tohumlamasi: {Added} eklendi, {Filled} bos satir dolduruldu.", added, filled);
            }
        }

        // CS collation sonrasi atlanan buyuk/kucuk harf varyantlarini dene.
        await SeedCaseVariantsAsync(ct);

        await ReloadAsync(ct);
    }

    /// <summary>
    /// Ilk turda IgnoreCase ile atlanan (Error vs ERROR) satirlari ayri context'te,
    /// tek tek ekler. Collation CI ise unique ihlali yutulur; CS ise satirlar yazilir.
    /// </summary>
    private async Task SeedCaseVariantsAsync(CancellationToken ct)
    {
        await using var probe = await dbFactory.CreateDbContextAsync(ct);

        var existing = await probe.Translations
            .AsNoTracking()
            .Select(row => new { row.Culture, row.Key })
            .ToListAsync(ct);

        var presentOrdinal = new HashSet<(string Culture, string Key)>(
            existing.Select(row => (row.Culture, row.Key)),
            StringTupleComparer.Ordinal);

        var presentIgnoreCase = new HashSet<(string Culture, string Key)>(
            existing.Select(row => (row.Culture, row.Key)),
            StringTupleComparer.OrdinalIgnoreCase);

        var pending = new List<(string Culture, string Key, string? Value)>();

        foreach (var (culture, entries) in TranslationDefaults.Seed)
        {
            foreach (var (key, value) in entries)
            {
                // Tam eslesme var → tamam.
                if (presentOrdinal.Contains((culture, key)))
                {
                    continue;
                }

                // IgnoreCase eslesme yok → birinci turda eklenmeliydi; atla.
                if (!presentIgnoreCase.Contains((culture, key)))
                {
                    continue;
                }

                // Ayni metnin farkli harf varyanti (Error varken ERROR).
                pending.Add((culture, key, value));
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        var added = 0;

        foreach (var (culture, key, value) in pending)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.Translations.Add(new Translation
            {
                Culture = culture,
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            try
            {
                await db.SaveChangesAsync(ct);
                added++;
            }
            catch (DbUpdateException)
            {
                // CI collation: unique ihlali beklenen.
            }
        }

        if (added > 0)
        {
            logger.LogInformation("Ceviri tohumlamasi (harf varyantlari): {Added} eklendi.", added);
        }
        else
        {
            logger.LogDebug(
                "Harf duyarli ceviri varyantlari yazilamadi (collation CI olabilir); atlandi.");
        }
    }

    private sealed class StringTupleComparer(StringComparison comparison)
        : IEqualityComparer<(string Culture, string Key)>
    {
        public static StringTupleComparer Ordinal { get; } = new(StringComparison.Ordinal);
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new(StringComparison.OrdinalIgnoreCase);

        public bool Equals((string Culture, string Key) x, (string Culture, string Key) y) =>
            string.Equals(x.Culture, y.Culture, comparison)
            && string.Equals(x.Key, y.Key, comparison);

        public int GetHashCode((string Culture, string Key) obj) =>
            HashCode.Combine(
                StringComparer.FromComparison(comparison).GetHashCode(obj.Culture),
                StringComparer.FromComparison(comparison).GetHashCode(obj.Key));
    }
}
