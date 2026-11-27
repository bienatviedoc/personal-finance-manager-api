using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
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
/// <see cref="EraseDeactivatedUsersHandler"/> erases everything of theirs. Answers null when nobody is signed in. The
/// request is recorded in the user's audit log in the transaction that deactivates them.
/// </summary>
public sealed class DeleteUserHandler(
    IValidator<DeleteUserCommand> validator, ICurrentUser currentUser, IUsers users, ISessions sessions, IAuditLog auditLog, IUnitOfWork unitOfWork,
    TimeProvider clock)
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

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await users.DeactivateAsync(user.Id, clock.GetUtcNow());
            await auditLog.DeletionRequestedAsync(user.Id, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await sessions.SignOutAsync();
        return new Done();
    }
}
