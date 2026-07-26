using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Infrastructure.Caching;

/// <summary>
/// <see cref="IDistributedCache"/> uzerine ince bir sarmalayici: JSON serilestirme ve
/// "yoksa uret" (get-or-create) mantigi tek yerde. Sagalayici Memory ya da Redis olabilir;
/// cagri yerleri bunu bilmez.
///
/// Redis gecici olarak erisilemezse istek **hata vermez**: cache atlanir ve deger uretilir.
/// Onbellek bir hizlandirma katmanidir, dogruluk kaynagi degildir.
/// </summary>
public sealed class CacheService(
    IDistributedCache cache,
    IOptions<CacheOptions> options,
    ILogger<CacheService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CacheOptions _options = options.Value;

    public bool UsesRedis => _options.UsesRedis;

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? lifetime = null,
        CancellationToken ct = default)
    {
        try
        {
            if (await cache.GetStringAsync(key, ct) is { Length: > 0 } cached
                && JsonSerializer.Deserialize<T>(cached, SerializerOptions) is { } value)
            {
                return value;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Onbellek okunamadi ({Key}); deger yeniden uretiliyor.", key);
        }

        var created = await factory(ct);

        try
        {
            await cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(created, SerializerOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = lifetime ?? TimeSpan.FromSeconds(_options.DefaultSeconds),
                },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Onbellege yazilamadi ({Key}).", key);
        }

        return created;
    }

    /// <summary>Veri degistiginde ilgili anahtari dusur (or. baglanti eklendiginde).</summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Onbellek anahtari silinemedi ({Key}).", key);
        }
    }

    /// <summary>Tanilama ekrani icin: onbellek gercekten yazip okuyabiliyor mu?</summary>
    public async Task<CacheHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        var probeKey = "health:probe";
        var expected = Guid.NewGuid().ToString("n");

        try
        {
            await cache.SetStringAsync(
                probeKey,
                expected,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
                ct);

            var actual = await cache.GetStringAsync(probeKey, ct);
            await cache.RemoveAsync(probeKey, ct);

            return new CacheHealth(
                _options.Provider.ToString(),
                _options.UsesRedis,
                actual == expected,
                actual == expected ? null : "Yazilan deger geri okunamadi.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CacheHealth(_options.Provider.ToString(), _options.UsesRedis, false, ex.Message);
        }
    }
}

/// <param name="Provider">Yapilandirmada secilen sagalayici adi.</param>
/// <param name="UsesRedis">Gercekten Redis'e mi baglaniyor?</param>
/// <param name="Working">Yaz/oku denemesi basarili mi?</param>
public sealed record CacheHealth(string Provider, bool UsesRedis, bool Working, string? Error);

/// <summary>Onbellek anahtarlari tek yerde: yazan ve dusuren kod ayni ismi kullanir.</summary>
public static class CacheKeys
{
    public const string ProjectNames = "dashboard:project-names";
    public const string ConnectionNames = "connections:names";
}
