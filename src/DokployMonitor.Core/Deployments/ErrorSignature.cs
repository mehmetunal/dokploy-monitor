namespace DokployMonitor.Core.Deployments;

/// <summary>
/// Normalize edilmis hata mesaji imzasi. Ayni kok nedene sahip hatalari
/// (degisken GUID/port/timestamp'ler temizlenerek) tek satirda gruplamak icin.
/// </summary>
public class ErrorSignature
{
    /// <summary>Normalize edilmis mesajin SHA-256 ozeti (ilk 16 byte, hex).</summary>
    public required string Hash { get; set; }

    /// <summary>Gruplama icin kullanilan normalize mesaj.</summary>
    public required string NormalizedMessage { get; set; }

    /// <summary>Kullaniciya gosterilecek ornek (ham) mesaj.</summary>
    public required string SampleMessage { get; set; }

    public int OccurrenceCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Bu hatayi en son veren servisin adi (hizli teshis icin).</summary>
    public string? LastServiceName { get; set; }
}
