using FluentValidation;

namespace DokployMonitor.Web.Models;

/// <summary>Giris formu.</summary>
public sealed class LoginInput
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public sealed class LoginInputValidator : AbstractValidator<LoginInput>
{
    public LoginInputValidator()
    {
        RuleFor(input => input.Email)
            .NotEmpty().WithMessage("E-posta zorunlu.")
            .EmailAddress().WithMessage("Gecerli bir e-posta adresi girin.");

        RuleFor(input => input.Password)
            .NotEmpty().WithMessage("Parola zorunlu.");
    }
}
