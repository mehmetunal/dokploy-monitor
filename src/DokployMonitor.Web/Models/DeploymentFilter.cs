using DokployMonitor.Core.Deployments;
using FluentValidation;

namespace DokployMonitor.Web.Models;

/// <summary>Deployment gecmisi ekraninin sorgu parametreleri (query string'ten baglanir).</summary>
public sealed class DeploymentFilter
{
    public string? Project { get; init; }

    /// <summary>running | done | error | cancelled | unknown — bos ise tum durumlar.</summary>
    public string? Status { get; init; }

    /// <summary>Servis / proje / hata metninde arama.</summary>
    public string? Q { get; init; }

    /// <summary>Bu tarihten itibaren (gun basi, sunucunun yerel saatine gore).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Bu tarihe kadar, gun sonu dahil.</summary>
    public DateOnly? To { get; init; }

    public int Take { get; init; } = 200;

    public DateTimeOffset? FromInstant => From is { } date ? Instant(date, TimeOnly.MinValue) : null;

    public DateTimeOffset? ToInstant => To is { } date ? Instant(date, TimeOnly.MaxValue) : null;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Project)
        && string.IsNullOrWhiteSpace(Status)
        && string.IsNullOrWhiteSpace(Q)
        && From is null
        && To is null;

    /// <summary>
    /// Tarih kutulari kullanicinin gunlerini ifade eder; kayitlar UTC tutuldugu icin
    /// gun sinirlari sunucunun yerel saat diliminden UTC'ye cevrilir (ekranlardaki
    /// diger zamanlar da ToLocalTime ile gosteriliyor).
    /// </summary>
    private static DateTimeOffset Instant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}

public sealed class DeploymentFilterValidator : AbstractValidator<DeploymentFilter>
{
    private static readonly string[] AllowedStatuses = Enum.GetNames<DeploymentStatus>();

    public DeploymentFilterValidator()
    {
        RuleFor(filter => filter.Status)
            .Must(status => AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Bilinmeyen durum. Gecerli degerler: {string.Join(", ", AllowedStatuses.Select(s => s.ToLowerInvariant()))}.")
            .When(filter => !string.IsNullOrWhiteSpace(filter.Status));

        RuleFor(filter => filter.Project)
            .MaximumLength(200)
            .WithMessage("En fazla 200 karakter olabilir.");

        RuleFor(filter => filter.Q)
            .MaximumLength(200)
            .WithMessage("Arama metni en fazla 200 karakter olabilir.");

        RuleFor(filter => filter.Take)
            .InclusiveBetween(1, 500)
            .WithMessage("1-500 arasinda olmali.");

        RuleFor(filter => filter.To)
            .GreaterThanOrEqualTo(filter => filter.From!.Value)
            .WithMessage("Bitis tarihi baslangictan once olamaz.")
            .When(filter => filter.From is not null && filter.To is not null);
    }
}
