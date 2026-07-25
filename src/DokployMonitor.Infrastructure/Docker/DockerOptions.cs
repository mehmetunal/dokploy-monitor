using FluentValidation;

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
    public DockerOptionsValidator()
    {
        RuleFor(options => options.SocketPath)
            .NotEmpty()
            .WithMessage("Docker soket yolu zorunlu (varsayilan /var/run/docker.sock).")
            .When(options => options.Enabled);

        RuleFor(options => options.ApiVersion)
            .Matches("^v[0-9]+\\.[0-9]+$")
            .WithMessage("Surum `v1.44` biciminde olmali.")
            .When(options => options.Enabled);

        RuleFor(options => options.TimeoutSeconds)
            .InclusiveBetween(2, 120)
            .WithMessage("2-120 saniye arasinda olmali.");

        RuleFor(options => options.MaxTailLines)
            .InclusiveBetween(100, 50_000)
            .WithMessage("100-50000 arasinda olmali.");
    }
}
