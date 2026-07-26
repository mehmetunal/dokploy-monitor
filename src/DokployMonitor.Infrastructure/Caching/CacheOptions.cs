using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Infrastructure.Caching;

public enum CacheProvider
{
    /// <summary>Uygulama icinde, tek konteynerde yeterli (varsayilan).</summary>
    Memory = 0,

    /// <summary>Redis; birden fazla ornek calisiyorsa ya da yeniden baslatmada cache korunacaksa.</summary>
    Redis = 1,
}

/// <summary>
/// Onbellek ayarlari. Kod her zaman <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// kullanir; sagalayici yalnizca burada secilir, cagri yerleri degismez.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public CacheProvider Provider { get; set; } = CacheProvider.Memory;

    /// <summary>Redis baglanti dizesi (or. <c>redis:6379</c> ya da <c>redis:6379,password=...</c>).</summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>Ayni Redis'i paylasan uygulamalar birbirine karismasin diye anahtar oneki.</summary>
    public string InstanceName { get; set; } = "dokploy-monitor:";

    /// <summary>Varsayilan yasam suresi (sn). Kisa listeler icin 30 sn yeterli.</summary>
    public int DefaultSeconds { get; set; } = 30;

    /// <summary>Redis secilmis ve baglanti dizesi verilmis mi?</summary>
    public bool UsesRedis => Provider == CacheProvider.Redis && !string.IsNullOrWhiteSpace(RedisConnectionString);
}

public sealed class CacheOptionsValidator : AbstractValidator<CacheOptions>
{
    public CacheOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.Provider)
            .IsInEnum()
            .WithMessage(_ => text["Valid values: Memory, Redis."]);

        // Redis secildiyse adres zorunlu: sessizce bellege dusmek yerine acilista uyar.
        RuleFor(options => options.RedisConnectionString)
            .NotEmpty()
            .WithMessage(_ => text["A connection string is required when Provider=Redis (e.g. redis:6379)."])
            .When(options => options.Provider == CacheProvider.Redis);

        RuleFor(options => options.InstanceName)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(options => options.DefaultSeconds)
            .InclusiveBetween(1, 3600)
            .WithMessage(_ => text["Must be between 1 and 3600 seconds."]);
    }
}
