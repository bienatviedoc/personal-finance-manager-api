using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>What one run of the integrity check came to.</summary>
/// <param name="Users">How many users' transactions it checked.</param>
/// <param name="Transactions">How many transactions it checked, deleted ones included.</param>
/// <param name="Drift">Everything it found that does not add up, each with the user it belongs to; empty when everything does.</param>
public sealed record IntegrityReport(int Users, int Transactions, IReadOnlyList<UserDrift> Drift);

/// <summary>One thing that does not add up in one user's transactions.</summary>
public sealed record UserDrift(Guid UserId, Drift Drift);

/// <summary>
/// Verifies that every user's saved transactions still add up (spec sections 4 and 10): each transaction's
/// lines sum to its amount, each transfer has its two matching legs, and each account's balance, worked out
/// as the account list works it out, is what its transactions come to (<see cref="TransactionIntegrity"/>).
/// The worker runs it every night; no user can. It changes nothing: what it finds is for a person to look into.
/// </summary>
public sealed class IntegrityCheck(ITransactionRepository transactions, IAccountTransactions balances, IActForUser actFor, IUnitOfWork unitOfWork)
{
    /// <summary>Runs for no user, so it acts for each user who has transactions in turn.</summary>
    public async Task<IntegrityReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var (users, checkedTransactions, drift) = (0, 0, new List<UserDrift>());
        foreach (var userId in await transactions.UsersWithTransactionsAsync(cancellationToken))
        {
            using (actFor.ActFor(userId))
            {
                var ofUser = await transactions.ListEveryAsync(cancellationToken);
                var shown = await balances.NetByAccountAsync([.. ofUser.Select(transaction => transaction.AccountId).Distinct()], cancellationToken);
                drift.AddRange(TransactionIntegrity.Check(ofUser, shown).Select(found => new UserDrift(userId, found)));
                (users, checkedTransactions) = (users + 1, checkedTransactions + ofUser.Count);
            }

            // The next user's rows are read on their own, and this one's are not kept in memory meanwhile.
            unitOfWork.DiscardChanges();
        }

        return new IntegrityReport(users, checkedTransactions, drift);
    }
}
