using DokployMonitor.Core.Dokploy;
using FluentValidation;

namespace DokployMonitor.Web.Models;

public sealed class ConnectionListViewModel
{
    public required IReadOnlyList<DokployConnection> Connections { get; init; }
    public required ConnectionInput NewConnection { get; init; }

    /// <summary>Baglanti basina veritabanindaki deployment sayisi.</summary>
    public required IReadOnlyDictionary<string, int> DeploymentCounts { get; init; }
}

/// <summary>Yeni/duzenlenen Dokploy baglantisi formu.</summary>
public sealed class ConnectionInput
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Duzenlemede bos birakilirsa mevcut anahtar korunur.</summary>
    public string? ApiKey { get; set; }

    public bool Enabled { get; set; } = true;
    public bool AllowInvalidCertificates { get; set; }
    public bool ForceLegacyDiscovery { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxParallelRequests { get; set; } = 4;
}

public sealed class ConnectionInputValidator : AbstractValidator<ConnectionInput>
{
    public ConnectionInputValidator()
    {
        RuleFor(input => input.Name)
            .NotEmpty().WithMessage("Baglanti adi zorunlu.")
            .MaximumLength(128);

        RuleFor(input => input.BaseUrl)
            .NotEmpty().WithMessage("Adres zorunlu (or. http://dokploy:3000).");

        RuleFor(input => input.BaseUrl)
            .Must(BeAbsoluteHttpUrl)
            .WithMessage("Mutlak bir http/https adresi olmali.")
            .When(input => !string.IsNullOrWhiteSpace(input.BaseUrl));

        RuleFor(input => input.BaseUrl)
            .Must(url => !url!.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Panelin koku yazilmali, sonuna /api eklenmemeli.")
            .When(input => !string.IsNullOrWhiteSpace(input.BaseUrl));

        // Yeni kayitta anahtar zorunlu; duzenlemede bos birakilirsa mevcut anahtar korunur.
        RuleFor(input => input.ApiKey)
            .NotEmpty().WithMessage("API anahtari zorunlu.")
            .When(input => string.IsNullOrWhiteSpace(input.Id));

        RuleFor(input => input.TimeoutSeconds)
            .InclusiveBetween(5, 120)
            .WithMessage("5-120 saniye arasinda olmali.");

        RuleFor(input => input.MaxParallelRequests)
            .InclusiveBetween(1, 16)
            .WithMessage("1-16 arasinda olmali.");
    }

    private static bool BeAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
