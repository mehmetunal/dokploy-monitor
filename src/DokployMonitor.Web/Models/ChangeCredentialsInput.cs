using DokployMonitor.Web.Options;
using FluentValidation;
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
    public ChangeCredentialsInputValidator(IOptions<AuthOptions> options)
    {
        var minimumLength = Math.Clamp(options.Value.MinimumPasswordLength, 8, 64);

        RuleFor(input => input.CurrentPassword)
            .NotEmpty().WithMessage("Mevcut parola zorunlu.");

        RuleFor(input => input.NewEmail)
            .NotEmpty().WithMessage("Yeni e-posta zorunlu.")
            .EmailAddress().WithMessage("Gecerli bir e-posta adresi girin.");

        RuleFor(input => input.NewPassword)
            .NotEmpty().WithMessage("Yeni parola zorunlu.")
            .MinimumLength(minimumLength).WithMessage($"En az {minimumLength} karakter olmali.")
            .Matches("[A-Z]").WithMessage("En az bir buyuk harf icermeli.")
            .Matches("[a-z]").WithMessage("En az bir kucuk harf icermeli.")
            .Matches("[0-9]").WithMessage("En az bir rakam icermeli.");

        RuleFor(input => input.ConfirmPassword)
            .Equal(input => input.NewPassword)
            .WithMessage("Parola tekrari ayni olmali.");

        RuleFor(input => input.NewPassword)
            .NotEqual(input => input.CurrentPassword)
            .WithMessage("Yeni parola mevcut paroladan farkli olmali.")
            .When(input => !string.IsNullOrEmpty(input.CurrentPassword));
    }
}
