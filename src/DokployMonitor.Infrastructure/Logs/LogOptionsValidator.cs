using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Infrastructure.Logs;

/// <summary>`Logs` bolumunun dogrulamasi.</summary>
public sealed class LogOptionsValidator : AbstractValidator<LogOptions>
{
    public LogOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.MountPath)
            .NotEmpty()
            .WithMessage(_ => text["The mount point of the Dokploy log folder inside the container is required."]);

        RuleFor(options => options.HostPath)
            .NotEmpty()
            .WithMessage(_ => text["The root directory used in Dokploy logPath values is required (default /etc/dokploy/logs)."]);

        RuleFor(options => options.ArchivePath)
            .NotEmpty()
            .WithMessage(_ => text["The archive directory is required; failed deployment logs are copied there."]);

        RuleFor(options => options.DefaultTailLines)
            .InclusiveBetween(50, 20_000)
            .WithMessage(_ => text["Must be between 50 and 20000 lines."]);

        RuleFor(options => options.PollIntervalMs)
            .InclusiveBetween(100, 10_000)
            .WithMessage(_ => text["Must be between 100 and 10000 ms; a shorter value wastes disk I/O."]);
    }
}
