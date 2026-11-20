using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Turns two-factor sign-in off for the signed-in user. Asks for the password: a session left open is not enough.</summary>
public sealed record TurnOffTwoFactorCommand(string Password)
{
    public string Password { get; } = Password ?? "";
}

public sealed class TurnOffTwoFactorValidator : AbstractValidator<TurnOffTwoFactorCommand>
{
    public TurnOffTwoFactorValidator() => RuleFor(command => command.Password).Required();
}

/// <summary>Answers null when nobody is signed in. Turning it off ends the user's other sessions; this one carries on.</summary>
public sealed class TurnOffTwoFactorHandler(
    IValidator<TurnOffTwoFactorCommand> validator, ICurrentUser currentUser, IUsers users, ITwoFactor twoFactor, ISessions sessions)
    : CommandHandler<TurnOffTwoFactorCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(TurnOffTwoFactorCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.FindByIdAsync(userId) is null)
        {
            return (Done?)null;
        }

        if (!await users.CheckPasswordAsync(userId, command.Password))
        {
            return Result<Done?>.Invalid(PasswordErrors.Incorrect(nameof(command.Password)));
        }

        await twoFactor.TurnOffAsync(userId);
        await sessions.RefreshAsync(userId);
        return new Done();
    }
}

/// <summary>The error for a field that asked the signed-in user for their password and got another.</summary>
public static class PasswordErrors
{
    public const string IncorrectMessage = "The password is incorrect.";

    public static IReadOnlyDictionary<string, string[]> Incorrect(string property) =>
        new Dictionary<string, string[]> { [property] = [IncorrectMessage] };
}
