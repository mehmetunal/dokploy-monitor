using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Web.Options;
using FluentValidation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Models;

/// <summary>Kullanici listesi satiri.</summary>
public sealed record UserRow
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool MustChangeCredentials { get; init; }
    public bool LockedOut { get; init; }
    public bool IsCurrentUser { get; init; }
}

public sealed class UserListViewModel
{
    public required IReadOnlyList<UserRow> Users { get; init; }
    public required CreateUserInput NewUser { get; init; }
}

/// <summary>Yeni kullanici formu. Kayit ekrani yok; kullanicilar buradan olusturulur.</summary>
public sealed class CreateUserInput
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Password { get; set; }
    public string Role { get; set; } = MonitorRoles.Viewer;

    /// <summary>true ise kullanici ilk giriste kimlik bilgilerini degistirmek zorunda.</summary>
    public bool MustChangeCredentials { get; set; } = true;
}

public sealed class CreateUserInputValidator : AbstractValidator<CreateUserInput>
{
    public CreateUserInputValidator(IOptions<AuthOptions> options, IStringLocalizer<SharedResource> text)
    {
        var minimumLength = Math.Clamp(options.Value.MinimumPasswordLength, 8, 64);

        RuleFor(input => input.Email)
            .NotEmpty().WithMessage(_ => text["Email is required."])
            .EmailAddress().WithMessage(_ => text["Enter a valid email address."]);

        RuleFor(input => input.DisplayName)
            .MaximumLength(100);

        RuleFor(input => input.Password)
            .NotEmpty().WithMessage(_ => text["Password is required."])
            .MinimumLength(minimumLength).WithMessage(_ => text["Must be at least {0} characters.", minimumLength])
            .Matches("[A-Z]").WithMessage(_ => text["Must contain at least one upper case letter."])
            .Matches("[a-z]").WithMessage(_ => text["Must contain at least one lower case letter."])
            .Matches("[0-9]").WithMessage(_ => text["Must contain at least one digit."]);

        RuleFor(input => input.Role)
            .Must(MonitorRoles.All.Contains)
            .WithMessage(_ => text["Valid roles: {0}.", string.Join(", ", MonitorRoles.All)]);
    }
}
