using FluentValidation;

namespace DokployMonitor.Web.Models;

/// <summary>Hata analizi ekraninin sorgu parametreleri.</summary>
public sealed class ErrorFilter
{
    public string? Project { get; init; }

    /// <summary>Son kac gun bakilacak. 0 = tum zamanlar.</summary>
    public int Days { get; init; }

    public DateTimeOffset? Since => Days > 0 ? DateTimeOffset.UtcNow.AddDays(-Days) : null;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Project) && Days == 0;
}

public sealed class ErrorFilterValidator : AbstractValidator<ErrorFilter>
{
    /// <summary>Ekrandaki acilir kutuyla ayni degerler; serbest gun sayisi kabul edilmez.</summary>
    public static readonly int[] AllowedDays = [0, 1, 7, 30, 90, 365];

    public ErrorFilterValidator()
    {
        RuleFor(filter => filter.Days)
            .Must(AllowedDays.Contains)
            .WithMessage($"Gecerli degerler: {string.Join(", ", AllowedDays)} (0 = tum zamanlar).");

        RuleFor(filter => filter.Project)
            .MaximumLength(200)
            .WithMessage("En fazla 200 karakter olabilir.");
    }
}
