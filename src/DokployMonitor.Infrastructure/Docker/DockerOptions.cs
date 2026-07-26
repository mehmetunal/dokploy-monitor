using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Infrastructure.Docker;

/// <summary>
/// Container loglarinin okundugu Docker Engine API ayarlari.
/// Konteynere mount: <c>-v /var/run/docker.sock:/var/run/docker.sock:ro</c>
/// </summary>
public sealed class DockerOptions
{
    public const string SectionName = "Docker";

    /// <summary>false ise container logu hic denenmez, yalnizca build logu kullanilir.</summary>
    public bool Enabled { get; set; } = true;

    public string SocketPath { get; set; } = "/var/run/docker.sock";

    /// <summary>Engine API surumu. Eski daemon'larda dusurulebilir.</summary>
    public string ApiVersion { get; set; } = "v1.44";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Bir istekte cekilecek en fazla satir (Engine'e gonderilen tail siniri).</summary>
    public int MaxTailLines { get; set; } = 2000;
}

public sealed class DockerOptionsValidator : AbstractValidator<DockerOptions>
{
    public DockerOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.SocketPath)
            .NotEmpty()
            .WithMessage(_ => text["The Docker socket path is required (default /var/run/docker.sock)."])
            .When(options => options.Enabled);

        RuleFor(options => options.ApiVersion)
            .Matches("^v[0-9]+\\.[0-9]+$")
            .WithMessage(_ => text["The version must look like `v1.44`."])
            .When(options => options.Enabled);

        RuleFor(options => options.TimeoutSeconds)
            .InclusiveBetween(2, 120)
            .WithMessage(_ => text["Must be between 2 and 120 seconds."]);

        RuleFor(options => options.MaxTailLines)
            .InclusiveBetween(100, 50_000)
            .WithMessage(_ => text["Must be between 100 and 50000."]);
    }
}
