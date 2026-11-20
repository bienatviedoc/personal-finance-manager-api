using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>
/// Replaces the signed-in user's recovery codes, for when they are used up or lost. Asks for the password: recovery
/// codes stand in for the authenticator, so a session left open must not be enough to get them.
/// </summary>
public sealed record NewRecoveryCodesCommand(string Password)
{
    public string Password { get; } = Password ?? "";
}

public sealed class NewRecoveryCodesValidator : AbstractValidator<NewRecoveryCodesCommand>
{
    public NewRecoveryCodesValidator() => RuleFor(command => command.Password).Required();
}

public abstract record NewRecoveryCodesOutcome
{
    /// <summary>The new codes, shown this once. The old ones no longer work.</summary>
    public sealed record Replaced(RecoveryCodesDetails Codes) : NewRecoveryCodesOutcome;

    /// <summary>Two-factor sign-in is off, so there is nothing for recovery codes to stand in for.</summary>
    public sealed record TwoFactorOff : NewRecoveryCodesOutcome;

    /// <summary>Nobody is signed in (or their user is gone).</summary>
    public sealed record NotSignedIn : NewRecoveryCodesOutcome;
}

public sealed class NewRecoveryCodesHandler(
    IValidator<NewRecoveryCodesCommand> validator, ICurrentUser currentUser, IUsers users, ITwoFactor twoFactor)
    : CommandHandler<NewRecoveryCodesCommand, NewRecoveryCodesOutcome>(validator)
{
    protected override async Task<Result<NewRecoveryCodesOutcome>> ExecuteAsync(NewRecoveryCodesCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user)
        {
            return new NewRecoveryCodesOutcome.NotSignedIn();
        }

        if (!user.TwoFactorEnabled)
        {
            return new NewRecoveryCodesOutcome.TwoFactorOff();
        }

        if (!await users.CheckPasswordAsync(userId, command.Password))
        {
            return Result<NewRecoveryCodesOutcome>.Invalid(PasswordErrors.Incorrect(nameof(command.Password)));
        }

        return new NewRecoveryCodesOutcome.Replaced(new RecoveryCodesDetails(await twoFactor.NewRecoveryCodesAsync(userId)));
    }
}
