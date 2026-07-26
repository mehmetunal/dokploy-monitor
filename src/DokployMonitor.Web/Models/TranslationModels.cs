using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Core.Localization;
using DokployMonitor.Web.Options;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Web.Models;

public sealed class TranslationListViewModel
{
    /// <summary>Duzenlenen dil (kaynak dil de secilebilir).</summary>
    public required string Culture { get; init; }

    public required IReadOnlyList<Translation> Rows { get; init; }

    /// <summary>Yalnizca cevrilmemis satirlar gosteriliyor mu?</summary>
    public bool OnlyMissing { get; init; }

    public string? Search { get; init; }

    public int TotalCount { get; init; }
    public int MissingCount { get; init; }

    public DateTimeOffset? LoadedAt { get; init; }
    public required PageInfo Page { get; init; }

    public IReadOnlyList<(string Code, string NativeName)> Cultures =>
        LocalizationSetup.SupportedCultures;
}

/// <summary>Yeni ceviri anahtari ekleme formu.</summary>
public sealed class TranslationInput
{
    public string? Culture { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
}

public sealed class TranslationInputValidator : AbstractValidator<TranslationInput>
{
    public TranslationInputValidator(IStringLocalizer<SharedResource> text)
    {
        RuleFor(input => input.Culture)
            .NotEmpty()
            .Must(LocalizationSetup.IsSupported)
            .WithMessage(_ => text["Unsupported language code."]);

        RuleFor(input => input.Key)
            .NotEmpty().WithMessage(_ => text["The key (source text) is required."])
            .MaximumLength(256);

        RuleFor(input => input.Value)
            .MaximumLength(4000);
    }
}
