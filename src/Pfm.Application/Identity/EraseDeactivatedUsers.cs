using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>Erases every deactivated user whose 30 days to cancel are over (spec section 8, Privacy).</summary>
public sealed record EraseDeactivatedUsersCommand;

/// <summary>Nothing to check: it takes no input. Kept so every command has its validator.</summary>
public sealed class EraseDeactivatedUsersValidator : AbstractValidator<EraseDeactivatedUsersCommand>;

/// <param name="Users">How many users were erased.</param>
public sealed record DeactivatedUsersErased(int Users);

/// <summary>
/// Runs for no user, in the worker, so it acts for each user in turn. Everything a user owns and then the user
/// themselves go in one transaction: a user is erased whole or, when something fails, not at all, and the next
/// run tries again. The files they attached are deleted from object storage before that. A user who signed in within their 30 days is no longer deactivated and is never listed.
/// </summary>
public sealed class EraseDeactivatedUsersHandler(
    IValidator<EraseDeactivatedUsersCommand> validator, IUsers users, IUserData userData, IAttachmentFiles attachmentFiles, IActForUser actFor, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EraseDeactivatedUsersCommand, DeactivatedUsersErased>(validator)
{
    protected override async Task<Result<DeactivatedUsersErased>> ExecuteAsync(EraseDeactivatedUsersCommand command, CancellationToken cancellationToken)
    {
        var due = await users.ListDeactivatedSinceAsync(clock.GetUtcNow() - Deactivation.CancellationPeriod, cancellationToken);
        foreach (var userId in due)
        {
            using var _ = actFor.ActFor(userId);

            // The files first: storage is no part of the transaction. Should the rows then fail to go, the next
            // run finds the user again and no file left to delete; the other order could leave files nothing
            // names any more, for good.
            await attachmentFiles.DeleteEverythingOfAsync(userId, cancellationToken);
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            await userData.EraseEverythingAsync(cancellationToken);
            await users.EraseAsync(userId);
            await transaction.CommitAsync(cancellationToken);
        }

        return new DeactivatedUsersErased(due.Count);
    }
}
