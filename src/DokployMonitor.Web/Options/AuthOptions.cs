using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Web.Options;

/// <summary>
/// Panel girisi ayarlari. Kayit ekrani yoktur: ilk yonetici acilista buradan olusturulur.
/// Parola verilmezse guclu bir parola uretilir ve **bir kez** log'a yazilir.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string AdminEmail { get; set; } = "admin@trimango.local";

    /// <summary>Bos ise ilk acilista rastgele uretilir ve log'a yazilir.</summary>
    public string? AdminPassword { get; set; }

    public string AdminDisplayName { get; set; } = "Yonetici";

    /// <summary>
    /// Identity parola uzunlugu kurali. Varsayilan 8: kurulum parolasi
    /// <c>Super123!</c> (9 karakter) bu kurala uyar.
    /// </summary>
    public int MinimumPasswordLength { get; set; } = 8;

    /// <summary>Oturum cerezinin gecerlilik suresi (gun).</summary>
    public int SessionDays { get; set; } = 7;
}

public sealed class AuthOptionsValidator : AbstractValidator<AuthOptions>
{
    public AuthOptionsValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(options => options.AdminEmail)
            .NotEmpty().WithMessage(_ => text["The administrator email is required."])
            .EmailAddress().WithMessage(_ => text["Must be a valid email address."]);

        RuleFor(options => options.AdminDisplayName)
            .NotEmpty().WithMessage(_ => text["The administrator name is required."])
            .MaximumLength(100);

        RuleFor(options => options.MinimumPasswordLength)
            .InclusiveBetween(8, 64)
            .WithMessage(_ => text["Must be between 8 and 64."]);

        RuleFor(options => options.SessionDays)
            .InclusiveBetween(1, 365)
            .WithMessage(_ => text["Must be between 1 and 365 days."]);

        RuleFor(options => options.AdminPassword)
            .MinimumLength(8)
            .WithMessage(_ => text["If set it must be at least 8 characters; leave empty to generate one."])
            .When(options => !string.IsNullOrEmpty(options.AdminPassword));
    }
}
