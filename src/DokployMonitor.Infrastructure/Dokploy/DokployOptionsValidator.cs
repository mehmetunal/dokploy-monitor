using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Infrastructure.Dokploy;

/// <summary>
/// `Dokploy` bolumunun dogrulamasi. Hatali baglanti ayariyla acilan bir konteyner
/// saatlerce bos pano gosterebildigi icin bu kontroller acilista yapilir.
///
/// Bolum **tamamen bos** olabilir: baglantilar artik veritabaninda tutuluyor ve
/// panelin Baglantilar ekranindan yonetiliyor (bkz. ConnectionService). Bu yuzden
/// BaseUrl/ApiKey yalnizca **digeri verilmisse** zorunludur — yarim yapilandirma
/// sessizce yok sayilmak yerine acilista hata verir.
/// </summary>
public sealed class DokployOptionsValidator : AbstractValidator<DokployOptions>
{
    public DokployOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.BaseUrl)
            .NotEmpty()
            .WithMessage(_ => text["Required. On the same host the internal address is preferred: http://dokploy:3000"])
            .When(options => !string.IsNullOrWhiteSpace(options.ApiKey));

        RuleFor(options => options.BaseUrl)
            .Must(BeAbsoluteHttpUrl)
            .WithMessage(_ => text["Must be an absolute http/https address (e.g. https://dokploy.example.com)."])
            .When(options => !string.IsNullOrWhiteSpace(options.BaseUrl));

        RuleFor(options => options.BaseUrl)
            .Must(url => !url.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            .WithMessage(_ => text["Enter the panel root without a trailing /api; the client appends /api/ itself."])
            .When(options => !string.IsNullOrWhiteSpace(options.BaseUrl));

        RuleFor(options => options.ApiKey)
            .NotEmpty()
            .WithMessage(_ => text["Required. Create it with Dokploy > Settings > API Keys > Generate API Key."])
            .When(options => !string.IsNullOrWhiteSpace(options.BaseUrl));

        RuleFor(options => options.TimeoutSeconds)
            .InclusiveBetween(5, 120)
            .WithMessage(_ => text["Must be between 5 and 120 seconds."]);

        RuleFor(options => options.MaxParallelRequests)
            .InclusiveBetween(1, 16)
            .WithMessage(_ => text["Must be between 1 and 16; a high value strains Dokploy in legacy mode."]);
    }

    private static bool BeAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
