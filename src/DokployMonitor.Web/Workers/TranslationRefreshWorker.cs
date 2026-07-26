using DokployMonitor.Infrastructure.Localization;

namespace DokployMonitor.Web.Workers;

/// <summary>
/// Ceviri anlik goruntusunu periyodik olarak tazeler ve ekranlarda gorulen ama
/// cevrilmemis anahtarlari veritabanina yazar.
///
/// Panelden yapilan duzenleme aninda uygulanir (kaydet -> ReloadAsync); bu isci
/// **coklu ornek** kurulumunda diger orneklerin degisiklikleri yakalamasi ve eksik
/// anahtarlarin toplanmasi icindir.
/// </summary>
public sealed class TranslationRefreshWorker(
    TranslationStore store,
    ILogger<TranslationRefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }

                await store.ReloadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ceviri tazeleme dongusunde hata.");
            }
        }
    }
}
