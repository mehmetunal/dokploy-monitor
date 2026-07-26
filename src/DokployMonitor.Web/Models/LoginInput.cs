using DokployMonitor.Infrastructure.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

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
    public LoginInputValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(input => input.Email)
            .NotEmpty().WithMessage(_ => text["Email is required."])
            .EmailAddress().WithMessage(_ => text["Enter a valid email address."]);

        RuleFor(input => input.Password)
            .NotEmpty().WithMessage(_ => text["Password is required."]);
    }
}
