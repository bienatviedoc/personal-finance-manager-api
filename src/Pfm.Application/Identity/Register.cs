using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <param name="Locale">
/// The language tag (<c>en</c> or <c>vi</c>) the user registered in: saved as their profile's locale, and the
/// language of their default categories. When left out, the profile has no locale and the categories are in English.
/// </param>
public sealed record RegisterCommand(string Email, string Password, string? Locale = null)
{
    public string Email { get; } = EmailAddress.Normalize(Email);

    public string Password { get; } = Password ?? "";

    /// <summary>The locale sent, read once; null when none was sent or it is not one the app speaks.</summary>
    public Locale? ParsedLocale { get; } = Locales.TryParse(Locale, out var locale) ? locale : null;
}

/// <summary>Checks the email and password alone, so the answer cannot depend on whether the email already has a user.</summary>
public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator(IPasswordPolicy passwords)
    {
        RuleFor(command => command.Email).Must(EmailAddress.IsValid).WithMessage("Enter a valid email address.");

        RuleFor(command => command.Locale).IsLocale(command => command.ParsedLocale);

        RuleFor(command => command.Password).CustomAsync(async (password, context, _) =>
        {
            foreach (var problem in await passwords.CheckAsync(context.InstanceToValidate.Email, password))
            {
                context.AddFailure(problem);
            }
        });
    }
}

/// <summary>
/// Answers the same way whether or not the email already has a user, so it cannot be used to find
/// out who is registered. An existing user is told by email instead, and nothing about them changes.
/// A new user keeps the locale they registered in, and starts with the default categories in it.
/// </summary>
public sealed class RegisterHandler(
    IValidator<RegisterCommand> validator, IUsers users, IIdentityEmails emails, IDefaultCategories defaultCategories, IUnitOfWork unitOfWork)
    : CommandHandler<RegisterCommand, Done>(validator)
{
    protected override async Task<Result<Done>> ExecuteAsync(RegisterCommand command, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(command.Email) is { } existing)
        {
            return await AlreadyRegisteredAsync(existing, command.Password, cancellationToken);
        }

        NewUser created;
        // The user and their default categories exist together or not at all.
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            created = await users.CreateAsync(command.Email, command.Password, command.ParsedLocale);
            if (created.User is { } newUser)
            {
                await defaultCategories.CreateForNewUserAsync(newUser.Id, command.ParsedLocale ?? Locale.En, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        // Another request may have registered this email a moment ago: answer as for any existing email.
        if (created.EmailTaken && await users.FindByEmailAsync(command.Email) is { } registeredMeanwhile)
        {
            return await AlreadyRegisteredAsync(registeredMeanwhile, command.Password, cancellationToken);
        }

        if (created.User is not { } user)
        {
            return Result<Done>.Invalid(new Dictionary<string, string[]> { [nameof(RegisterCommand.Email)] = [.. created.Errors] });
        }

        var token = await users.CreateEmailConfirmationTokenAsync(user.Id);
        await emails.SendVerificationAsync(user, token, cancellationToken);
        return new Done();
    }

    /// <summary>
    /// The answer for an email that already has a user: the same as for a new one, and as slow, since a
    /// new registration hashes the password. The user is told by email; nothing about them changes.
    /// </summary>
    private async Task<Result<Done>> AlreadyRegisteredAsync(UserDetails existing, string password, CancellationToken cancellationToken)
    {
        users.HashAndDiscard(password);
        await emails.SendAlreadyRegisteredAsync(existing, cancellationToken);
        return new Done();
    }
}
