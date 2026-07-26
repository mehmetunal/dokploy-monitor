using DokployMonitor.Core.Dokploy;
using DokployMonitor.Infrastructure.Caching;
using DokployMonitor.Infrastructure.Dokploy;
using DokployMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Services;

/// <summary>
/// Izlenen Dokploy baglantilarini yonetir. Baglantilar veritabaninda tutulur; ortam
/// degiskenleriyle gelen tek baglanti (Dokploy__BaseUrl / Dokploy__ApiKey) ilk acilista
/// "Varsayilan" adiyla ice aktarilir, boylece mevcut kurulumlar bozulmaz.
/// </summary>
public sealed class ConnectionService(
    MonitorDbContext db,
    CacheService cache,
    IOptions<DokployOptions> dokployOptions,
    ILogger<ConnectionService> logger)
{
    public const string ImportedConnectionName = "Varsayilan";

    public Task<List<DokployConnection>> GetAllAsync(CancellationToken ct = default) =>
        db.Connections.OrderBy(connection => connection.Name).ToListAsync(ct);

    public Task<List<DokployConnection>> GetEnabledAsync(CancellationToken ct = default) =>
        db.Connections.Where(connection => connection.Enabled)
            .OrderBy(connection => connection.Name)
            .ToListAsync(ct);

    /// <summary>
    /// Ekranlarda kimlik yerine ad gostermek icin: baglantiId -> ad.
    /// Pano her 5 saniyede bir cizildigi icin onbelleklenir (bkz. CacheKeys.ConnectionNames).
    /// </summary>
    public Task<Dictionary<string, string>> GetNamesAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync(
            CacheKeys.ConnectionNames,
            token => db.Connections.ToDictionaryAsync(
                connection => connection.Id,
                connection => connection.Name,
                token),
            ct: ct);

    /// <summary>Baglanti eklendi/degisti/silindi: ad onbellegini dusur.</summary>
    public Task InvalidateNamesAsync(CancellationToken ct = default) =>
        cache.RemoveAsync(CacheKeys.ConnectionNames, ct);

    /// <summary>
    /// Ortam degiskenlerindeki baglantiyi (varsa) veritabanina tasir ve eski deployment
    /// kayitlarini bu baglantiyla eslestirir. Zaten baglanti varsa hicbir sey yapmaz.
    /// </summary>
    public async Task ImportFromConfigurationAsync(CancellationToken ct = default)
    {
        if (await db.Connections.AnyAsync(ct))
        {
            return;
        }

        var options = dokployOptions.Value;
        if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            logger.LogWarning(
                "Tanimli Dokploy baglantisi yok. Panelde Baglantilar ekranindan ekleyin "
                + "ya da Dokploy__BaseUrl / Dokploy__ApiKey degiskenlerini verin.");
            return;
        }

        var connection = new DokployConnection
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = ImportedConnectionName,
            BaseUrl = options.BaseUrl,
            ApiKey = options.ApiKey,
            Enabled = true,
            AllowInvalidCertificates = options.AllowInvalidCertificates,
            ForceLegacyDiscovery = options.ForceLegacyDiscovery,
            TimeoutSeconds = options.TimeoutSeconds,
            MaxParallelRequests = options.MaxParallelRequests,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Connections.Add(connection);
        await db.SaveChangesAsync(ct);

        // Coklu baglanti oncesinde toplanan kayitlar bu baglantiya ait sayilir.
        var backfilled = await db.Deployments
            .Where(deployment => deployment.ConnectionId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.ConnectionId, connection.Id), ct);

        logger.LogInformation(
            "Ortam degiskenlerindeki Dokploy baglantisi '{Name}' olarak ice aktarildi "
            + "({Count} mevcut deployment kaydi bu baglantiya baglandi).",
            connection.Name,
            backfilled);
    }

    /// <summary>Senkronizasyon sonucunu baglanti satirina yazar (Baglantilar ekraninda gorunur).</summary>
    public async Task RecordSyncResultAsync(string connectionId, string? error, CancellationToken ct = default)
    {
        await db.Connections
            .Where(connection => connection.Id == connectionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.LastSyncAt, DateTimeOffset.UtcNow)
                    .SetProperty(c => c.LastSyncError, error),
                ct);
    }
}
