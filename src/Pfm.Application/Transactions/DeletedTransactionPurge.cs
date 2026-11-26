using Pfm.Application.Abstractions;
using Pfm.Application.Transactions.Attachments;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>What one run of the purge came to.</summary>
/// <param name="Transactions">How many deleted transactions it removed for good, each leg of a transfer counted.</param>
public sealed record DeletedTransactionsPurged(int Transactions);

/// <summary>
/// Removes for good every transaction deleted more than 30 days ago, which can no longer be restored (spec
/// section 3, rule 10), with its lines and attachments. The worker runs it; no user can.
/// </summary>
/// <remarks>
/// An attachment's foreign key restricts deleting its transaction, so the files go from storage first, then
/// the attachments' rows and the transactions in one save. Should the save fail, the next run finds the same
/// transactions again and no file left to delete; the other order could leave files nothing names any more,
/// for good. The legs of a transfer were deleted together, so they are removed together.
/// </remarks>
public sealed class DeletedTransactionPurge(
    ITransactionRepository transactions, IAttachmentRepository attachments, IObjectStorage storage, IActForUser actFor, IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>
    /// Runs for no user, so it acts for each user who has such transactions in turn. One user whose
    /// transactions cannot be removed holds up no other: the rest are removed, and then the failures are thrown together.
    /// </summary>
    public async Task<DeletedTransactionsPurged> RunAsync(CancellationToken cancellationToken = default)
    {
        var deletedBefore = clock.GetUtcNow() - Transaction.RecoveryPeriod;
        var (removed, failures) = (0, new List<Exception>());
        foreach (var userId in await transactions.UsersWithDeletedBeforeAsync(deletedBefore, cancellationToken))
        {
            try
            {
                using (actFor.ActFor(userId))
                {
                    removed += await PurgeAsync(deletedBefore, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(new InvalidOperationException($"Removing the deleted transactions of user {userId} failed; the next run tries again.", exception));
            }

            // The next user's rows are read on their own, with nothing left over from this one.
            unitOfWork.DiscardChanges();
        }

        if (failures.Count > 0)
        {
            throw new AggregateException($"Removed {removed} deleted transaction(s); {failures.Count} user(s) failed.", failures);
        }

        return new DeletedTransactionsPurged(removed);
    }

    private async Task<int> PurgeAsync(DateTimeOffset deletedBefore, CancellationToken cancellationToken)
    {
        var due = await transactions.ListDeletedBeforeAsync(deletedBefore, cancellationToken);
        var attached = await attachments.ListOfAsync([.. due.Select(transaction => transaction.Id)], cancellationToken);
        foreach (var attachment in attached)
        {
            await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
            attachments.Remove(attachment);
        }

        foreach (var transaction in due)
        {
            transactions.Remove(transaction);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return due.Count;
    }
}
