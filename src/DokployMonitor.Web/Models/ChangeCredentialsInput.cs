using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Web.Options;
using FluentValidation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Models;

/// <summary>Zorunlu kimlik degisimi formu: e-posta ve parola birlikte degistirilir.</summary>
public sealed class ChangeCredentialsInput
{
    public string? CurrentPassword { get; set; }
    public string? NewEmail { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}

public sealed class ChangeCredentialsInputValidator : AbstractValidator<ChangeCredentialsInput>
{
    public ChangeCredentialsInputValidator(IOptions<AuthOptions> options, IStringLocalizer<SharedResource> text)
    {
        var minimumLength = Math.Clamp(options.Value.MinimumPasswordLength, 8, 64);

        RuleFor(input => input.CurrentPassword)
            .NotEmpty().WithMessage(_ => text["The current password is required."]);

        RuleFor(input => input.NewEmail)
            .NotEmpty().WithMessage(_ => text["A new email is required."])
            .EmailAddress().WithMessage(_ => text["Enter a valid email address."]);

        RuleFor(input => input.NewPassword)
            .NotEmpty().WithMessage(_ => text["A new password is required."])
            .MinimumLength(minimumLength).WithMessage(_ => text["Must be at least {0} characters.", minimumLength])
            .Matches("[A-Z]").WithMessage(_ => text["Must contain at least one upper case letter."])
            .Matches("[a-z]").WithMessage(_ => text["Must contain at least one lower case letter."])
            .Matches("[0-9]").WithMessage(_ => text["Must contain at least one digit."]);

        RuleFor(input => input.ConfirmPassword)
            .Equal(input => input.NewPassword)
            .WithMessage(_ => text["The password confirmation must match."]);

        // Bilincli olarak "mevcut paroladan farkli olmali" kurali yok: bu ekran bir onay
        // adimi, kullanici ayni kimlik bilgileriyle devam etmeyi secebilir.
    }
}
