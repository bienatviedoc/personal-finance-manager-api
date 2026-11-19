using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Confirms a user's email address with the user id and token from the emailed link.</summary>
public sealed record VerifyEmailCommand(Guid UserId, string Token);

public sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailValidator()
    {
        RuleFor(command => command.UserId).Required();
        RuleFor(command => command.Token).Required();
    }
}

public enum VerifyEmailOutcome
{
    Confirmed,

    /// <summary>The link was tampered with, has expired, or names no user. Which one is not said.</summary>
    LinkInvalid,
}

public sealed class VerifyEmailHandler(IValidator<VerifyEmailCommand> validator, IUsers users)
    : CommandHandler<VerifyEmailCommand, VerifyEmailOutcome>(validator)
{
    protected override async Task<Result<VerifyEmailOutcome>> ExecuteAsync(VerifyEmailCommand command, CancellationToken cancellationToken) =>
        await users.ConfirmEmailAsync(command.UserId, command.Token) ? VerifyEmailOutcome.Confirmed : VerifyEmailOutcome.LinkInvalid;
}
