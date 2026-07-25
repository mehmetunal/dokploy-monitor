using FluentValidation;

namespace DokployMonitor.Infrastructure.Dokploy;

/// <summary>
/// `Dokploy` bolumunun dogrulamasi. Hatali baglanti ayariyla acilan bir konteyner
/// saatlerce bos pano gosterebildigi icin bu kontroller acilista yapilir.
/// </summary>
public sealed class DokployOptionsValidator : AbstractValidator<DokployOptions>
{
    public DokployOptionsValidator()
    {
        RuleFor(options => options.BaseUrl)
            .NotEmpty()
            .WithMessage("Zorunlu. Ayni sunucuda ic ag adresi tercih edilir: http://dokploy:3000");

        RuleFor(options => options.BaseUrl)
            .Must(BeAbsoluteHttpUrl)
            .WithMessage("Mutlak bir http/https adresi olmali (or. https://dokploy.sirketiniz.com).")
            .When(options => !string.IsNullOrWhiteSpace(options.BaseUrl));

        RuleFor(options => options.BaseUrl)
            .Must(url => !url.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Panelin koku yazilmali, sonuna /api eklenmemeli; istemci /api/ onekini kendisi ekler.")
            .When(options => !string.IsNullOrWhiteSpace(options.BaseUrl));

        RuleFor(options => options.ApiKey)
            .NotEmpty()
            .WithMessage("Zorunlu. Dokploy > Settings > API Keys > Generate API Key ile uretilir.");

        RuleFor(options => options.TimeoutSeconds)
            .InclusiveBetween(5, 120)
            .WithMessage("5-120 saniye arasinda olmali.");

        RuleFor(options => options.MaxParallelRequests)
            .InclusiveBetween(1, 16)
            .WithMessage("1-16 arasinda olmali; yuksek deger legacy modda Dokploy'u yorar.");
    }

    private static bool BeAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
