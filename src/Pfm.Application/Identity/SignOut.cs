using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Ends the current request's session.</summary>
public sealed record SignOutCommand;

/// <summary>Nothing to check: signing out takes no input. Kept so every command has its validator.</summary>
public sealed class SignOutValidator : AbstractValidator<SignOutCommand>;

public sealed class SignOutHandler(IValidator<SignOutCommand> validator, ISessions sessions)
    : CommandHandler<SignOutCommand, Done>(validator)
{
    protected override async Task<Result<Done>> ExecuteAsync(SignOutCommand command, CancellationToken cancellationToken)
    {
        await sessions.SignOutAsync();
        return new Done();
    }
}
