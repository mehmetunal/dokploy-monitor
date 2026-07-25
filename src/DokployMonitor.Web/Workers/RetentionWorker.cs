using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Workers;

/// <summary>
/// Eski deployment kayitlarini ve arsivlenmis loglari temizler.
/// Gunde bir kez calisir; RetentionDays = 0 ise hicbir sey silinmez.
/// </summary>
public sealed class RetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MonitorOptions> options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private readonly MonitorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            logger.LogInformation("Kayit temizleme kapali (RetentionDays = 0).");
            return;
        }

        // Acilista hemen calistirma; uygulama isinsin.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                await CleanupAsync(stoppingToken);

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kayit temizleme sirasinda hata.");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

        // Arsivlenmis log dosyalarini once diskten sil (kayit silinince yol kaybolur).
        var archived = await db.Deployments
            .Where(d => d.CreatedAt < cutoff && d.ArchivedLogPath != null)
            .Select(d => d.ArchivedLogPath!)
            .ToListAsync(ct);

        foreach (var path in archived)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Arsiv log dosyasi silinemedi: {Path}", path);
            }
        }

        var deleted = await db.Deployments.Where(d => d.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            logger.LogInformation("{Count} eski deployment kaydi temizlendi.", deleted);
        }
    }
}
