using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>
/// Starts turning two-factor sign-in on: answers the key for the signed-in user's authenticator app. Nothing
/// changes for signing in until <see cref="TurnOnTwoFactorCommand"/> confirms the app shows the right codes.
/// </summary>
public sealed record SetUpTwoFactorCommand;

/// <summary>Nothing to check: setting up takes no input. Kept so every command has its validator.</summary>
public sealed class SetUpTwoFactorValidator : AbstractValidator<SetUpTwoFactorCommand>;

public abstract record SetUpTwoFactorOutcome
{
    /// <param name="Key">The key, for typing into an authenticator app by hand.</param>
    /// <param name="AuthenticatorUri">The same key as the <c>otpauth://</c> link an authenticator app reads from a QR code.</param>
    public sealed record Ready(string Key, string AuthenticatorUri) : SetUpTwoFactorOutcome;

    /// <summary>Two-factor sign-in is on already. A new key would stop the user's authenticator working, so none is made.</summary>
    public sealed record AlreadyOn : SetUpTwoFactorOutcome;
}

/// <summary>Answers null when nobody is signed in (or their user is gone).</summary>
public sealed class SetUpTwoFactorHandler(
    IValidator<SetUpTwoFactorCommand> validator, ICurrentUser currentUser, IUsers users, ITwoFactor twoFactor)
    : CommandHandler<SetUpTwoFactorCommand, SetUpTwoFactorOutcome?>(validator)
{
    /// <summary>What an authenticator app lists the codes under.</summary>
    public const string Issuer = "Personal Finance Manager";

    protected override async Task<Result<SetUpTwoFactorOutcome?>> ExecuteAsync(SetUpTwoFactorCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user)
        {
            return (SetUpTwoFactorOutcome?)null;
        }

        if (user.TwoFactorEnabled)
        {
            return new SetUpTwoFactorOutcome.AlreadyOn();
        }

        var key = await twoFactor.AuthenticatorKeyAsync(userId);
        return new SetUpTwoFactorOutcome.Ready(key, AuthenticatorUri(user.Email, key));
    }

    // The Key URI format authenticator apps read: six digits every thirty seconds, their default.
    private static string AuthenticatorUri(string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
}
