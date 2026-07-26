using DokployMonitor.Infrastructure.Dokploy;
using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Infrastructure.Validation;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Options;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Tests;

// Validator'lar mesajlari localizer'dan alir; testler kaynak dil davranisini kullanir.

/// <summary>
/// Yapilandirma hatalari uygulama acilirken yakalanmali; yanlis ayarla ayaga kalkan bir
/// konteyner saatlerce sessizce bos pano gosterir.
/// </summary>
public sealed class OptionsValidationTests
{
    [Fact]
    public void Dokploy_ayarlari_gecerli_degerleri_kabul_eder()
    {
        var result = new DokployOptionsValidator(new SourceLanguageLocalizer()).Validate(new DokployOptions
        {
            BaseUrl = "http://dokploy:3000",
            ApiKey = "dokploy_monitor_abc",
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "anahtar", nameof(DokployOptions.BaseUrl))]
    [InlineData("dokploy:3000", "anahtar", nameof(DokployOptions.BaseUrl))]
    [InlineData("http://dokploy:3000/api", "anahtar", nameof(DokployOptions.BaseUrl))]
    [InlineData("http://dokploy:3000", "", nameof(DokployOptions.ApiKey))]
    public void Dokploy_ayarlari_hatali_degerleri_reddeder(string baseUrl, string apiKey, string expectedProperty)
    {
        var result = new DokployOptionsValidator(new SourceLanguageLocalizer()).Validate(new DokployOptions
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == expectedProperty);
    }

    [Fact]
    public void Log_ayarlari_mantiksiz_araliklari_reddeder()
    {
        var result = new LogOptionsValidator(new SourceLanguageLocalizer()).Validate(new LogOptions
        {
            DefaultTailLines = 5,
            PollIntervalMs = 10,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LogOptions.DefaultTailLines));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LogOptions.PollIntervalMs));
    }

    [Fact]
    public void Monitor_varsayilanlari_gecerlidir()
    {
        Assert.True(new MonitorOptionsValidator(new SourceLanguageLocalizer()).Validate(new MonitorOptions()).IsValid);
    }

    [Fact]
    public void Aktif_polling_araligi_bos_araliktan_uzun_olamaz()
    {
        var result = new MonitorOptionsValidator(new SourceLanguageLocalizer()).Validate(new MonitorOptions
        {
            IdlePollSeconds = 5,
            ActivePollSeconds = 30,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(MonitorOptions.ActivePollSeconds));
    }

    [Fact]
    public void Webhook_tokeni_bos_olabilir_ama_kisa_olamaz()
    {
        var validator = new WebhookOptionsValidator(new SourceLanguageLocalizer());

        Assert.True(validator.Validate(new WebhookOptions { Token = null }).IsValid);
        Assert.True(validator.Validate(new WebhookOptions { Token = "" }).IsValid);
        Assert.False(validator.Validate(new WebhookOptions { Token = "kisa" }).IsValid);
        Assert.True(validator.Validate(new WebhookOptions { Token = new string('a', 64) }).IsValid);
    }

    [Fact]
    public void Deployment_filtresi_bilinmeyen_durumu_reddeder()
    {
        var validator = new DeploymentFilterValidator(new SourceLanguageLocalizer());

        Assert.True(validator.Validate(new DeploymentFilter()).IsValid);
        Assert.True(validator.Validate(new DeploymentFilter { Status = "ERROR" }).IsValid);
        Assert.False(validator.Validate(new DeploymentFilter { Status = "failed" }).IsValid);
        Assert.False(validator.Validate(new DeploymentFilter { Page = 0 }).IsValid);
        Assert.False(validator.Validate(new DeploymentFilter { Q = new string('x', 201) }).IsValid);
    }

    [Fact]
    public void Deployment_filtresi_ters_tarih_araligini_reddeder()
    {
        var validator = new DeploymentFilterValidator(new SourceLanguageLocalizer());
        var from = new DateOnly(2026, 7, 20);
        var to = new DateOnly(2026, 7, 10);

        Assert.True(validator.Validate(new DeploymentFilter { From = from, To = from }).IsValid);
        Assert.True(validator.Validate(new DeploymentFilter { From = to, To = from }).IsValid);
        Assert.True(validator.Validate(new DeploymentFilter { From = from }).IsValid);

        var result = validator.Validate(new DeploymentFilter { From = from, To = to });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DeploymentFilter.To));
    }

    [Fact]
    public void Deployment_filtresi_gun_sinirlarini_yerel_saate_gore_cevirir()
    {
        var filter = new DeploymentFilter { From = new DateOnly(2026, 7, 20), To = new DateOnly(2026, 7, 20) };

        var from = Assert.IsType<DateTimeOffset>(filter.FromInstant);
        var to = Assert.IsType<DateTimeOffset>(filter.ToInstant);

        Assert.Equal(new TimeOnly(0, 0), TimeOnly.FromDateTime(from.DateTime));
        Assert.True(to - from > TimeSpan.FromHours(23));
        Assert.True(to - from < TimeSpan.FromHours(24));
    }

    [Fact]
    public void Hata_filtresi_yalnizca_tanimli_gun_degerlerini_kabul_eder()
    {
        var validator = new ErrorFilterValidator(new SourceLanguageLocalizer());

        foreach (var days in ErrorFilterValidator.AllowedDays)
        {
            Assert.True(validator.Validate(new ErrorFilter { Days = days }).IsValid);
        }

        Assert.False(validator.Validate(new ErrorFilter { Days = 5 }).IsValid);
        Assert.False(validator.Validate(new ErrorFilter { Days = -1 }).IsValid);
    }

    [Fact]
    public void Hata_filtresinde_gun_degeri_baslangic_anina_cevrilir()
    {
        Assert.Null(new ErrorFilter { Days = 0 }.Since);

        var since = new ErrorFilter { Days = 7 }.Since;
        Assert.NotNull(since);
        Assert.InRange(
            DateTimeOffset.UtcNow - since.Value,
            TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1),
            TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Options_boru_hatti_gecersiz_yapilandirmada_hata_firlatir()
    {
        var services = new ServiceCollection();

        // Validator'lar mesajlari localizer'dan alir; testte kaynak dil davranisi yeterli.
        services.AddSingleton<IStringLocalizer<SharedResource>, SourceLanguageLocalizer>();
        services.AddValidatorsFromAssemblyContaining<DokployOptionsValidator>(ServiceLifetime.Singleton);
        services.AddOptions<DokployOptions>()
            .Configure(options =>
            {
                options.BaseUrl = string.Empty;
                options.ApiKey = string.Empty;
            })
            .ValidateWithFluentValidation();

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DokployOptions>>().Value);

        Assert.Contains("DokployOptions.BaseUrl", string.Join(" ", exception.Failures));
        Assert.Contains("DokployOptions.ApiKey", string.Join(" ", exception.Failures));
    }
}

/// <summary>
/// Pass-through localizer for tests: returns the key itself, which is exactly how the
/// application behaves for the source language (English).
/// </summary>
public sealed class SourceLanguageLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
