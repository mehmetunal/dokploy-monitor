using DokployMonitor.Infrastructure.Dokploy;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Infrastructure.Validation;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Options;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Tests;

/// <summary>
/// Yapilandirma hatalari uygulama acilirken yakalanmali; yanlis ayarla ayaga kalkan bir
/// konteyner saatlerce sessizce bos pano gosterir.
/// </summary>
public sealed class OptionsValidationTests
{
    [Fact]
    public void Dokploy_ayarlari_gecerli_degerleri_kabul_eder()
    {
        var result = new DokployOptionsValidator().Validate(new DokployOptions
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
        var result = new DokployOptionsValidator().Validate(new DokployOptions
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
        var result = new LogOptionsValidator().Validate(new LogOptions
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
        Assert.True(new MonitorOptionsValidator().Validate(new MonitorOptions()).IsValid);
    }

    [Fact]
    public void Aktif_polling_araligi_bos_araliktan_uzun_olamaz()
    {
        var result = new MonitorOptionsValidator().Validate(new MonitorOptions
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
        var validator = new WebhookOptionsValidator();

        Assert.True(validator.Validate(new WebhookOptions { Token = null }).IsValid);
        Assert.True(validator.Validate(new WebhookOptions { Token = "" }).IsValid);
        Assert.False(validator.Validate(new WebhookOptions { Token = "kisa" }).IsValid);
        Assert.True(validator.Validate(new WebhookOptions { Token = new string('a', 64) }).IsValid);
    }

    [Fact]
    public void Deployment_filtresi_bilinmeyen_durumu_reddeder()
    {
        var validator = new DeploymentFilterValidator();

        Assert.True(validator.Validate(new DeploymentFilter()).IsValid);
        Assert.True(validator.Validate(new DeploymentFilter { Status = "ERROR" }).IsValid);
        Assert.False(validator.Validate(new DeploymentFilter { Status = "failed" }).IsValid);
        Assert.False(validator.Validate(new DeploymentFilter { Take = 5000 }).IsValid);
        Assert.False(validator.Validate(new DeploymentFilter { Q = new string('x', 201) }).IsValid);
    }

    [Fact]
    public void Deployment_filtresi_ters_tarih_araligini_reddeder()
    {
        var validator = new DeploymentFilterValidator();
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
        var validator = new ErrorFilterValidator();

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
