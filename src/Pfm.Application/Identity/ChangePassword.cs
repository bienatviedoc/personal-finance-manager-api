using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Changes the signed-in user's password, given the one they have now.</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
{
    public string CurrentPassword { get; } = CurrentPassword ?? "";

    public string NewPassword { get; } = NewPassword ?? "";
}

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator(ICurrentUser currentUser, IUsers users, IPasswordPolicy passwords)
    {
        RuleFor(command => command.CurrentPassword).Required();

        RuleFor(command => command.NewPassword).CustomAsync(async (password, context, _) =>
        {
            // The policy may judge a password against the user's email; with nobody signed in, the handler refuses later.
            var email = (await users.FindSignedInAsync(currentUser))?.Email ?? "";
            foreach (var problem in await passwords.CheckAsync(email, password))
            {
                context.AddFailure(problem);
            }
        });
    }
}

/// <summary>
/// Answers null when nobody is signed in. A changed password ends the user's other sessions; this one carries on.
/// </summary>
public sealed class ChangePasswordHandler(
    IValidator<ChangePasswordCommand> validator, ICurrentUser currentUser, IUsers users, ISessions sessions)
    : CommandHandler<ChangePasswordCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (await users.FindSignedInAsync(currentUser) is not { } user)
        {
            return (Done?)null;
        }

        if (!await users.ChangePasswordAsync(user.Id, command.CurrentPassword, command.NewPassword))
        {
            return Result<Done?>.Invalid(PasswordErrors.Incorrect(nameof(command.CurrentPassword)));
        }

        await sessions.RefreshAsync(user.Id);
        return new Done();
    }
}
