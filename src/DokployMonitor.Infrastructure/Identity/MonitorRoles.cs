namespace DokployMonitor.Infrastructure.Identity;

/// <summary>
/// Panel rolleri.
///
/// <see cref="SuperAdmin"/>: kullanici yonetimi ve deployment mudahalesi (Durdur /
/// Yeniden Deploy / Replay) yalnizca bu roldedir. <see cref="Viewer"/>: salt okuma —
/// panolari, gecmisi, hatalari ve loglari gorur, aksiyon alamaz.
/// </summary>
public static class MonitorRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [SuperAdmin, Viewer];
}
