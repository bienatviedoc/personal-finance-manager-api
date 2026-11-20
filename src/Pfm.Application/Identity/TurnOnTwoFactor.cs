using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>
/// Turns two-factor sign-in on for the signed-in user, once a code proves their authenticator app holds the key
/// from <see cref="SetUpTwoFactorCommand"/>.
/// </summary>
public sealed record TurnOnTwoFactorCommand(string Code)
{
    public string Code { get; } = TwoFactorCodes.NormalizeCode(Code);
}

public sealed class TurnOnTwoFactorValidator : AbstractValidator<TurnOnTwoFactorCommand>
{
    public TurnOnTwoFactorValidator() => RuleFor(command => command.Code).Required();
}

/// <summary>
/// Answers the user's recovery codes, the only time they are shown, or null when nobody is signed in. Turning it on
/// ends the user's other sessions; this one carries on.
/// </summary>
public sealed class TurnOnTwoFactorHandler(
    IValidator<TurnOnTwoFactorCommand> validator, ICurrentUser currentUser, IUsers users, ITwoFactor twoFactor, ISessions sessions)
    : CommandHandler<TurnOnTwoFactorCommand, RecoveryCodesDetails?>(validator)
{
    public const string CodeIncorrectMessage = "That code is not right. Enter the newest code from your authenticator app.";

    protected override async Task<Result<RecoveryCodesDetails?>> ExecuteAsync(TurnOnTwoFactorCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user)
        {
            return (RecoveryCodesDetails?)null;
        }

        if (user.TwoFactorEnabled)
        {
            // Turning it on again would hand out new recovery codes for a code alone.
            return Refused("Two-factor sign-in is already on.");
        }

        if (!await twoFactor.CheckCodeAsync(userId, command.Code))
        {
            return Refused(CodeIncorrectMessage);
        }

        var recoveryCodes = await twoFactor.TurnOnAsync(userId);
        await sessions.RefreshAsync(userId);
        return new RecoveryCodesDetails(recoveryCodes);
    }

    private static Result<RecoveryCodesDetails?> Refused(string why) =>
        Result<RecoveryCodesDetails?>.Invalid(new Dictionary<string, string[]> { [nameof(TurnOnTwoFactorCommand.Code)] = [why] });
}
