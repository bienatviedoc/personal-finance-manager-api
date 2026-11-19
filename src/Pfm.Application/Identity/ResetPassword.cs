using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Sets a new password with the user id and token from the emailed reset link.</summary>
public sealed record ResetPasswordCommand(Guid UserId, string Token, string Password)
{
    public string Password { get; } = Password ?? "";
}

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator(IUsers users, IPasswordPolicy passwords)
    {
        RuleFor(command => command.UserId).Required();
        RuleFor(command => command.Token).Required();

        RuleFor(command => command.Password).CustomAsync(async (password, context, _) =>
        {
            // The policy may judge a password against the user's email; a link naming no user is refused later.
            var email = await users.FindByIdAsync(context.InstanceToValidate.UserId) is { } user ? user.Email : "";
            foreach (var problem in await passwords.CheckAsync(email, password))
            {
                context.AddFailure(problem);
            }
        });
    }
}

public enum ResetPasswordOutcome
{
    /// <summary>The password is changed, and every session the user had has ended.</summary>
    PasswordChanged,

    /// <summary>The link was tampered with, has been used, has expired, or names no user. Which one is not said.</summary>
    LinkInvalid,
}

public sealed class ResetPasswordHandler(IValidator<ResetPasswordCommand> validator, IUsers users)
    : CommandHandler<ResetPasswordCommand, ResetPasswordOutcome>(validator)
{
    protected override async Task<Result<ResetPasswordOutcome>> ExecuteAsync(ResetPasswordCommand command, CancellationToken cancellationToken) =>
        await users.ResetPasswordAsync(command.UserId, command.Token, command.Password)
            ? ResetPasswordOutcome.PasswordChanged
            : ResetPasswordOutcome.LinkInvalid;
}
