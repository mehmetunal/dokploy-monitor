using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Infrastructure.Validation;

/// <summary>
/// Bir FluentValidation validator'unu <see cref="IValidateOptions{TOptions}"/> boru hattina baglar.
/// <c>ValidateOnStart()</c> ile birlikte kullanildiginda yapilandirma hatalari uygulama
/// acilirken tek seferde ve tam mesajla raporlanir — konteyner yanlis ayarla ayaga kalkmaz.
/// </summary>
public sealed class FluentValidationOptions<TOptions>(string? name, IValidator<TOptions> validator)
    : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? optionsName, TOptions options)
    {
        // Ayni tip icin isimli birden fazla kayit olabilir; yalnizca kendi adimizi dogrula.
        if (name is not null && name != optionsName)
        {
            return ValidateOptionsResult.Skip;
        }

        var result = validator.Validate(options);
        if (result.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            result.Errors.Select(failure => $"{typeof(TOptions).Name}.{failure.PropertyName}: {failure.ErrorMessage}"));
    }
}

public static class FluentValidationOptionsExtensions
{
    /// <summary>
    /// Options tipini kendi <c>IValidator&lt;T&gt;</c>'i ile dogrula. Validator'lar
    /// singleton kaydedilmelidir; options dogrulamasi kok kapsamdan cozulur.
    /// </summary>
    public static OptionsBuilder<TOptions> ValidateWithFluentValidation<TOptions>(
        this OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(provider =>
            new FluentValidationOptions<TOptions>(builder.Name, provider.GetRequiredService<IValidator<TOptions>>()));

        return builder;
    }
}
