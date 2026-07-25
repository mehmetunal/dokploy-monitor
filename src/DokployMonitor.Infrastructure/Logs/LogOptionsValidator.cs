using FluentValidation;

namespace DokployMonitor.Infrastructure.Logs;

/// <summary>`Logs` bolumunun dogrulamasi.</summary>
public sealed class LogOptionsValidator : AbstractValidator<LogOptions>
{
    public LogOptionsValidator()
    {
        RuleFor(options => options.MountPath)
            .NotEmpty()
            .WithMessage("Dokploy log klasorunun konteyner icindeki mount noktasi zorunlu.");

        RuleFor(options => options.HostPath)
            .NotEmpty()
            .WithMessage("Dokploy'un logPath degerlerindeki kok dizin zorunlu (varsayilan /etc/dokploy/logs).");

        RuleFor(options => options.ArchivePath)
            .NotEmpty()
            .WithMessage("Arsiv dizini zorunlu; hatali deployment loglari buraya kopyalanir.");

        RuleFor(options => options.DefaultTailLines)
            .InclusiveBetween(50, 20_000)
            .WithMessage("50-20000 satir arasinda olmali.");

        RuleFor(options => options.PollIntervalMs)
            .InclusiveBetween(100, 10_000)
            .WithMessage("100-10000 ms arasinda olmali; daha kisa deger diski bosa yorar.");
    }
}
