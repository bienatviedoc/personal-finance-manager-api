using Pfm.Application.Abstractions;

namespace Pfm.Application.Imports;

/// <summary>
/// Removes every statement uploaded and never committed once it is older than the time uploads are kept, its file
/// with it: a statement can hold a full account number, which the app never stores for good (spec ACC-8). The worker
/// runs it; no user can.
/// </summary>
public sealed class AbandonedUploadSweep(IImportBatchRepository batches, IActForUser actFor, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>
    /// Runs for no user, so it acts for each user who has such a batch in turn. One user whose batches cannot be
    /// removed holds up no other: the rest are removed, and then the failures are thrown together. Answers how many
    /// batches it removed.
    /// </summary>
    /// <param name="keepFor">How long after it is uploaded a statement that is not committed is kept.</param>
    public async Task<int> RunAsync(TimeSpan keepFor, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(keepFor, TimeSpan.Zero);
        var uploadedBefore = clock.GetUtcNow() - keepFor;
        var (removed, failures) = (0, new List<Exception>());
        foreach (var userId in await batches.UsersWithUploadedBeforeAsync(uploadedBefore, cancellationToken))
        {
            try
            {
                using (actFor.ActFor(userId))
                {
                    var abandoned = await batches.ListUploadedBeforeAsync(uploadedBefore, cancellationToken);
                    foreach (var batch in abandoned)
                    {
                        batches.Remove(batch);
                    }

                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    removed += abandoned.Count;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(new InvalidOperationException($"Removing the abandoned uploads of user {userId} failed; the next run tries again.", exception));
            }

            // The next user's rows are read on their own, with nothing left over from this one.
            unitOfWork.DiscardChanges();
        }

        if (failures.Count > 0)
        {
            throw new AggregateException($"Removed {removed} abandoned upload(s); {failures.Count} user(s) failed.", failures);
        }

        return removed;
    }
}
