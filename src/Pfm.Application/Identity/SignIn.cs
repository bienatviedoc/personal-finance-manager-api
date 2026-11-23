using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <param name="TwoFactorCode">The code from the user's authenticator app, when they sign in with two factors.</param>
/// <param name="RecoveryCode">One of the user's recovery codes, in place of a code from the authenticator.</param>
public sealed record SignInCommand(string Email, string Password, string? TwoFactorCode = null, string? RecoveryCode = null)
{
    public string Email { get; } = EmailAddress.Normalize(Email);

    public string Password { get; } = Password ?? "";

    public string TwoFactorCode { get; } = TwoFactorCodes.NormalizeCode(TwoFactorCode);

    public string RecoveryCode { get; } = TwoFactorCodes.NormalizeRecoveryCode(RecoveryCode);
}

public sealed class SignInValidator : AbstractValidator<SignInCommand>
{
    public SignInValidator()
    {
        RuleFor(command => command.Email).Required();
        RuleFor(command => command.Password).Required();
    }
}

public enum SignInOutcome
{
    SignedIn,

    /// <summary>Signed in, and that cancelled the deletion the user had asked for: they were a deactivated user.</summary>
    SignedInAndDeletionCancelled,

    /// <summary>
    /// No user has the email, the password is wrong, signing in is locked for now after too many wrong
    /// attempts, or the user was deactivated more than 30 days ago and is waiting to be erased. Which one is
    /// not said.
    /// </summary>
    WrongEmailOrPassword,

    /// <summary>The password is right, but the user has not confirmed their email address yet.</summary>
    EmailNotConfirmed,

    /// <summary>The password is right; the user signs in with two factors and sent no code yet.</summary>
    TwoFactorRequired,

    /// <summary>The password is right, but the authenticator or recovery code is not.</summary>
    TwoFactorCodeIncorrect,
}

/// <summary>
/// Checks the password before anything else, so only someone who knows it learns whether the email address is
/// confirmed or a second factor is asked for. Unknown emails cost a password hash too, so timing reveals nothing.
/// A wrong password or code counts towards locking sign-in for the user (<see cref="IUsers.RecordFailedSignInAsync"/>);
/// while locked even the right password is refused, the same way a wrong one is.
/// </summary>
/// <remarks>
/// Signing in is how a deactivated user cancels their deletion (spec section 8, Privacy): only a sign-in that
/// gets all the way through does, so the password alone is not enough for a user who signs in with two factors.
/// Once their 30 days are over there is nothing left to cancel, and they are refused as a wrong password is.
/// </remarks>
public sealed class SignInHandler(
    IValidator<SignInCommand> validator, IUsers users, ITwoFactor twoFactor, ISessions sessions, TimeProvider clock)
    : CommandHandler<SignInCommand, SignInOutcome>(validator)
{
    protected override async Task<Result<SignInOutcome>> ExecuteAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email);
        if (user is null)
        {
            users.HashAndDiscard(command.Password);
            return SignInOutcome.WrongEmailOrPassword;
        }

        // Checked even when locked, so a locked user's answer takes as long as any other.
        var passwordIsRight = await users.CheckPasswordAsync(user.Id, command.Password);
        if (await users.IsLockedOutAsync(user.Id))
        {
            return SignInOutcome.WrongEmailOrPassword;
        }

        if (!passwordIsRight)
        {
            await users.RecordFailedSignInAsync(user.Id);
            return SignInOutcome.WrongEmailOrPassword;
        }

        if (user.DeactivatedAt is { } deactivatedAt && Deactivation.IsDueForErasure(deactivatedAt, clock.GetUtcNow()))
        {
            return SignInOutcome.WrongEmailOrPassword;
        }

        if (!user.EmailConfirmed)
        {
            return SignInOutcome.EmailNotConfirmed;
        }

        if (user.TwoFactorEnabled)
        {
            if (command.TwoFactorCode.Length == 0 && command.RecoveryCode.Length == 0)
            {
                return SignInOutcome.TwoFactorRequired;
            }

            var codeIsRight = command.TwoFactorCode.Length > 0
                ? await twoFactor.CheckCodeAsync(user.Id, command.TwoFactorCode)
                : await twoFactor.RedeemRecoveryCodeAsync(user.Id, command.RecoveryCode);
            if (!codeIsRight)
            {
                // Six digits can be guessed; counting these too stops someone who has the password from trying them all.
                await users.RecordFailedSignInAsync(user.Id);
                return SignInOutcome.TwoFactorCodeIncorrect;
            }
        }

        await users.ClearFailedSignInsAsync(user.Id);
        if (user.DeactivatedAt is not null)
        {
            await users.ReactivateAsync(user.Id);
        }

        await sessions.SignInAsync(user.Id);
        return user.DeactivatedAt is null ? SignInOutcome.SignedIn : SignInOutcome.SignedInAndDeletionCancelled;
    }
}
