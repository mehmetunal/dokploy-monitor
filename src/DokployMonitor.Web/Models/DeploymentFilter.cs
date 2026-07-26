using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Core.Deployments;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Web.Models;

/// <summary>Deployment gecmisi ekraninin sorgu parametreleri (query string'ten baglanir).</summary>
public sealed class DeploymentFilter
{
    public string? Project { get; init; }

    /// <summary>running | done | error | cancelled | unknown — bos ise tum durumlar.</summary>
    public string? Status { get; init; }

    /// <summary>Servis / proje / hata metninde arama.</summary>
    public string? Q { get; init; }

    /// <summary>Yalnizca bu Dokploy baglantisindan gelen kayitlar.</summary>
    public string? ConnectionId { get; init; }

    /// <summary>Bu tarihten itibaren (gun basi, sunucunun yerel saatine gore).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Bu tarihe kadar, gun sonu dahil.</summary>
    public DateOnly? To { get; init; }

    /// <summary>1-based page number; clamped by <see cref="PageInfo"/>.</summary>
    public int? Page { get; init; }

    /// <summary>Rows per page; only values from <see cref="PageInfo.AllowedSizes"/> are honoured.</summary>
    public int? Size { get; init; }

    public DateTimeOffset? FromInstant => From is { } date ? Instant(date, TimeOnly.MinValue) : null;

    public DateTimeOffset? ToInstant => To is { } date ? Instant(date, TimeOnly.MaxValue) : null;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Project)
        && string.IsNullOrWhiteSpace(Status)
        && string.IsNullOrWhiteSpace(Q)
        && string.IsNullOrWhiteSpace(ConnectionId)
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

    public DeploymentFilterValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(filter => filter.Status)
            .Must(status => AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage(_ => text["Unknown status. Valid values: {0}.",
                string.Join(", ", AllowedStatuses.Select(status => status.ToLowerInvariant()))])
            .When(filter => !string.IsNullOrWhiteSpace(filter.Status));

        RuleFor(filter => filter.Project)
            .MaximumLength(200)
            .WithMessage(_ => text["Must be at most 200 characters."]);

        RuleFor(filter => filter.Q)
            .MaximumLength(200)
            .WithMessage(_ => text["The search text must be at most 200 characters."]);

        RuleFor(filter => filter.Page)
            .GreaterThan(0)
            .WithMessage(_ => text["The page number must be greater than zero."])
            .When(filter => filter.Page is not null);

        RuleFor(filter => filter.To)
            .GreaterThanOrEqualTo(filter => filter.From!.Value)
            .WithMessage(_ => text["The end date cannot be before the start date."])
            .When(filter => filter.From is not null && filter.To is not null);
    }
}
