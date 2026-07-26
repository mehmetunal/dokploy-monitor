using FluentValidation;

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
    public AuthOptionsValidator()
    {
        RuleFor(options => options.AdminEmail)
            .NotEmpty().WithMessage("Yonetici e-postasi zorunlu.")
            .EmailAddress().WithMessage("Gecerli bir e-posta adresi olmali.");

        RuleFor(options => options.AdminDisplayName)
            .NotEmpty().WithMessage("Yonetici adi zorunlu.")
            .MaximumLength(100);

        RuleFor(options => options.MinimumPasswordLength)
            .InclusiveBetween(8, 64)
            .WithMessage("8-64 arasinda olmali.");

        RuleFor(options => options.SessionDays)
            .InclusiveBetween(1, 365)
            .WithMessage("1-365 gun arasinda olmali.");

        RuleFor(options => options.AdminPassword)
            .MinimumLength(8)
            .WithMessage("Tanimliysa en az 8 karakter olmali; bos birakilirsa parola uretilir.")
            .When(options => !string.IsNullOrEmpty(options.AdminPassword));
    }
}
