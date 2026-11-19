using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Domain;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>
/// Changes the signed-in user's profile settings. A setting left out (null) stays as it was.
/// Values arrive as sent, so a value that is not supported is a validation error naming its field.
/// </summary>
/// <param name="BaseCurrency">An ISO 4217 code the app supports, such as <c>VND</c>.</param>
/// <param name="TimeZone">An IANA time zone id, such as <c>Asia/Ho_Chi_Minh</c>.</param>
/// <param name="Locale">A language tag: <c>en</c> or <c>vi</c>.</param>
public sealed record UpdateProfileCommand(string? BaseCurrency = null, string? TimeZone = null, string? Locale = null);

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(command => command.BaseCurrency)
            .Must(code => Currencies.TryParse(code, out _))
            .When(command => command.BaseCurrency is not null)
            .WithMessage($"Choose one of: {string.Join(", ", Enum.GetNames<Currency>())}.");

        RuleFor(command => command.TimeZone)
            .Must(IsIanaTimeZone)
            .When(command => command.TimeZone is not null)
            .WithMessage("Choose a time zone such as Asia/Ho_Chi_Minh.");

        RuleFor(command => command.Locale)
            .Must(tag => Locales.TryParse(tag, out _))
            .When(command => command.Locale is not null)
            .WithMessage($"Choose one of: {string.Join(", ", Enum.GetValues<Locale>().Select(locale => locale.Tag()))}.");
    }

    private static bool IsIanaTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) && zone.HasIanaId;
}

public sealed class UpdateProfileHandler(ICurrentUser currentUser, IUserProfiles profiles)
{
    /// <summary>
    /// Applies a validated command. Returns the updated profile, or null when nobody is signed in (or
    /// their user is gone).
    /// </summary>
    /// <remarks>Spec rule 12 (the base currency locks once a budget exists) belongs here once budgets exist.</remarks>
    public async Task<UserProfile?> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await profiles.FindAsync(userId, cancellationToken) is not { } profile)
        {
            return null;
        }

        var settings = new ProfileSettings(
            command.BaseCurrency is null ? profile.BaseCurrency : Parse<Currency>(Currencies.TryParse, command.BaseCurrency),
            command.TimeZone ?? profile.TimeZone,
            command.Locale is null ? profile.Locale : Parse<Locale>(Locales.TryParse, command.Locale));

        await profiles.SaveAsync(userId, settings, cancellationToken);
        return profile with { BaseCurrency = settings.BaseCurrency, TimeZone = settings.TimeZone, Locale = settings.Locale };
    }

    private delegate bool TryParse<T>(string? value, out T result);

    private static T Parse<T>(TryParse<T> tryParse, string value) =>
        tryParse(value, out var result) ? result : throw new ArgumentException($"'{value}' was not validated before handling.", nameof(value));
}
