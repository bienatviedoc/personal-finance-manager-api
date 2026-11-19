using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

public sealed record SignInCommand(string Email, string Password)
{
    public string Email { get; } = EmailAddress.Normalize(Email);

    public string Password { get; } = Password ?? "";
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

    /// <summary>No user has the email, or the password is wrong. Which one is not said.</summary>
    WrongEmailOrPassword,

    /// <summary>The password is right, but the user has not confirmed their email address yet.</summary>
    EmailNotConfirmed,
}

/// <summary>
/// Checks the password before anything else, so only someone who knows it learns whether the
/// email address is confirmed. Unknown emails cost a password hash too, so timing reveals nothing.
/// </summary>
public sealed class SignInHandler(IValidator<SignInCommand> validator, IUsers users, ISessions sessions)
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

        if (!await users.CheckPasswordAsync(user.Id, command.Password))
        {
            return SignInOutcome.WrongEmailOrPassword;
        }

        if (!user.EmailConfirmed)
        {
            return SignInOutcome.EmailNotConfirmed;
        }

        await sessions.SignInAsync(user.Id);
        return SignInOutcome.SignedIn;
    }
}
