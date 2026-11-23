using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>
/// The signed-in user asks to be deleted (spec section 8, Privacy). Asks for the password: a session left open
/// is not enough.
/// </summary>
public sealed record DeleteUserCommand(string Password)
{
    public string Password { get; } = Password ?? "";
}

public sealed class DeleteUserValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserValidator() => RuleFor(command => command.Password).Required();
}

/// <summary>
/// Deactivates the user and ends every session they have, this one included. Nothing is erased yet: signing in
/// again within 30 days cancels the deletion (<see cref="SignInHandler"/>), and after that
/// <see cref="EraseDeactivatedUsersHandler"/> erases everything of theirs. Answers null when nobody is signed in.
/// </summary>
public sealed class DeleteUserHandler(
    IValidator<DeleteUserCommand> validator, ICurrentUser currentUser, IUsers users, ISessions sessions, TimeProvider clock)
    : CommandHandler<DeleteUserCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        if (await users.FindSignedInAsync(currentUser) is not { } user)
        {
            return (Done?)null;
        }

        if (!await users.CheckPasswordAsync(user.Id, command.Password))
        {
            return Result<Done?>.Invalid(PasswordErrors.Incorrect(nameof(command.Password)));
        }

        await users.DeactivateAsync(user.Id, clock.GetUtcNow());
        await sessions.SignOutAsync();
        return new Done();
    }
}
