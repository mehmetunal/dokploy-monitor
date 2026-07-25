using FluentValidation;

namespace DokployMonitor.Web.Options;

/// <summary>
/// `Monitor` bolumunun dogrulamasi. Polling araliklari dogrudan Dokploy API'sine
/// dusen yuku belirledigi icin ust/alt sinirlar zorlanir.
/// </summary>
public sealed class MonitorOptionsValidator : AbstractValidator<MonitorOptions>
{
    public MonitorOptionsValidator()
    {
        RuleFor(options => options.IdlePollSeconds)
            .InclusiveBetween(2, 3600)
            .WithMessage("2-3600 saniye arasinda olmali.");

        RuleFor(options => options.ActivePollSeconds)
            .InclusiveBetween(1, 600)
            .WithMessage("1-600 saniye arasinda olmali.");

        RuleFor(options => options.ActivePollSeconds)
            .LessThanOrEqualTo(options => options.IdlePollSeconds)
            .WithMessage("Aktif deployment varken bos zamandan daha seyrek sorgulanmamali; "
                + "IdlePollSeconds degerinden buyuk olamaz.");

        RuleFor(options => options.QueuePollSeconds)
            .InclusiveBetween(2, 3600)
            .WithMessage("2-3600 saniye arasinda olmali.");

        RuleFor(options => options.RecentCount)
            .InclusiveBetween(5, 500)
            .WithMessage("5-500 arasinda olmali.");

        RuleFor(options => options.RetentionDays)
            .InclusiveBetween(0, 3650)
            .WithMessage("0-3650 gun arasinda olmali (0 = hic silme).");

        RuleFor(options => options.FreshFinishWindowMinutes)
            .InclusiveBetween(1, 1440)
            .WithMessage("1-1440 dakika arasinda olmali.");
    }
}

/// <summary>
/// `Webhook` bolumunun dogrulamasi. Token bos olabilir — o zaman webhook ucu
/// kapalidir (404). Tanimliysa tahmin edilemeyecek uzunlukta olmali.
/// </summary>
public sealed class WebhookOptionsValidator : AbstractValidator<WebhookOptions>
{
    public WebhookOptionsValidator()
    {
        RuleFor(options => options.Token)
            .MinimumLength(16)
            .WithMessage("Tanimliysa en az 16 karakter olmali (or. `openssl rand -hex 32`). "
                + "Webhook'u kapatmak icin tamamen bos birakin.")
            .When(options => !string.IsNullOrEmpty(options.Token));
    }
}
