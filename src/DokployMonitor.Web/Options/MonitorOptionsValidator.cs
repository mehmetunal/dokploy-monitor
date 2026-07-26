using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Web.Options;

/// <summary>
/// `Monitor` bolumunun dogrulamasi. Polling araliklari dogrudan Dokploy API'sine
/// dusen yuku belirledigi icin ust/alt sinirlar zorlanir.
/// </summary>
public sealed class MonitorOptionsValidator : AbstractValidator<MonitorOptions>
{
    public MonitorOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.IdlePollSeconds)
            .InclusiveBetween(2, 3600)
            .WithMessage(_ => text["Must be between 2 and 3600 seconds."]);

        RuleFor(options => options.ActivePollSeconds)
            .InclusiveBetween(1, 600)
            .WithMessage(_ => text["Must be between 1 and 600 seconds."]);

        RuleFor(options => options.ActivePollSeconds)
            .LessThanOrEqualTo(options => options.IdlePollSeconds)
            .WithMessage("Must not be polled less often than when idle; "
                + "it cannot exceed IdlePollSeconds.");

        RuleFor(options => options.QueuePollSeconds)
            .InclusiveBetween(2, 3600)
            .WithMessage(_ => text["Must be between 2 and 3600 seconds."]);

        RuleFor(options => options.RecentCount)
            .InclusiveBetween(5, 500)
            .WithMessage(_ => text["Must be between 5 and 500."]);

        RuleFor(options => options.RetentionDays)
            .InclusiveBetween(0, 3650)
            .WithMessage(_ => text["Must be between 0 and 3650 days (0 = never delete)."]);

        RuleFor(options => options.FreshFinishWindowMinutes)
            .InclusiveBetween(1, 1440)
            .WithMessage(_ => text["Must be between 1 and 1440 minutes."]);
    }
}

/// <summary>
/// `Webhook` bolumunun dogrulamasi. Token bos olabilir — o zaman webhook ucu
/// kapalidir (404). Tanimliysa tahmin edilemeyecek uzunlukta olmali.
/// </summary>
public sealed class WebhookOptionsValidator : AbstractValidator<WebhookOptions>
{
    public WebhookOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.Token)
            .MinimumLength(16)
            .WithMessage("If set it must be at least 16 characters (e.g. `openssl rand -hex 32`). "
                + "Leave it completely empty to disable the webhook.")
            .When(options => !string.IsNullOrEmpty(options.Token));
    }
}
