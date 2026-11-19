using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>
/// Changes the signed-in user's profile settings. A setting left out (null) stays as it was.
/// Values arrive as sent and are read once, here, so a value that is not supported is a validation error
/// naming its field, and the handler uses exactly what the validator accepted.
/// </summary>
/// <param name="BaseCurrency">An ISO 4217 code the app supports, such as <c>VND</c>.</param>
/// <param name="TimeZone">An IANA time zone id, such as <c>Asia/Ho_Chi_Minh</c>.</param>
/// <param name="Locale">A language tag: <c>en</c> or <c>vi</c>.</param>
public sealed record UpdateProfileCommand(string? BaseCurrency = null, string? TimeZone = null, string? Locale = null)
{
    /// <summary>The currency sent; null when none was sent or it is not one the app supports.</summary>
    public Currency? ParsedBaseCurrency { get; } = Currencies.TryParse(BaseCurrency, out var currency) ? currency : null;

    /// <summary>The locale sent; null when none was sent or it is not one the app speaks.</summary>
    public Locale? ParsedLocale { get; } = Locales.TryParse(Locale, out var locale) ? locale : null;
}

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(command => command.BaseCurrency)
            .Must((command, _) => command.ParsedBaseCurrency is not null)
            .When(command => command.BaseCurrency is not null)
            .WithMessage($"Choose one of: {string.Join(", ", Enum.GetNames<Currency>())}.");

        RuleFor(command => command.TimeZone)
            .Must(IsIanaTimeZone)
            .When(command => command.TimeZone is not null)
            .WithMessage("Choose a time zone such as Asia/Ho_Chi_Minh.");

        RuleFor(command => command.Locale)
            .Must((command, _) => command.ParsedLocale is not null)
            .When(command => command.Locale is not null)
            .WithMessage($"Choose one of: {string.Join(", ", Enum.GetValues<Locale>().Select(locale => locale.Tag()))}.");
    }

    private static bool IsIanaTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) && zone.HasIanaId;
}

/// <summary>
/// Answers the updated profile, or null when nobody is signed in (or their user is gone).
/// </summary>
/// <remarks>Spec rule 12 (the base currency locks once a budget exists) belongs here once budgets exist.</remarks>
public sealed class UpdateProfileHandler(
    IValidator<UpdateProfileCommand> validator, ICurrentUser currentUser, IUserProfiles profiles, IUnitOfWork unitOfWork)
    : CommandHandler<UpdateProfileCommand, ProfileDetails?>(validator)
{
    private static readonly ProfileDetails? NoUser = null;

    protected override async Task<Result<ProfileDetails?>> ExecuteAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await profiles.FindAsync(userId, cancellationToken) is not { } profile)
        {
            return NoUser;
        }

        var settings = new ProfileSettings(
            command.ParsedBaseCurrency ?? profile.BaseCurrency,
            command.TimeZone ?? profile.TimeZone,
            command.ParsedLocale ?? profile.Locale);

        if (!await profiles.ChangeSettingsAsync(userId, settings, cancellationToken))
        {
            return NoUser;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return profile with { BaseCurrency = settings.BaseCurrency, TimeZone = settings.TimeZone, Locale = settings.Locale };
    }
}
