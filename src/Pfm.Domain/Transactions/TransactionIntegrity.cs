namespace Pfm.Domain.Transactions;

/// <summary>
/// Whether a user's transactions still add up, as the domain promises they do: a transaction's lines sum to
/// its amount (spec section 3, rule 5), a transfer is one Transfer out leg and one Transfer in leg that match
/// (rules 3 and 4), and each account's balance is what its transactions come to (section 4). The rules are
/// kept as each transaction is saved; this checks them again on what was saved, for the nightly integrity
/// check (section 10). It says what does not add up and never corrects it.
/// </summary>
public static class TransactionIntegrity
{
    /// <param name="transactions">Every one of the user's transactions, deleted ones included.</param>
    /// <param name="balances">
    /// What each account's transactions come to as balances are worked out for the user to see: income − expenses
    /// + refunds + transfers in − transfers out, deleted transactions left out. An account with none may be left out.
    /// </param>
    public static IReadOnlyList<Drift> Check(IReadOnlyCollection<Transaction> transactions, IReadOnlyDictionary<Guid, decimal> balances)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(balances);
        return [.. SplitsThatDrift(transactions), .. TransfersThatDrift(transactions), .. BalancesThatDrift(transactions, balances)];
    }

    /// <summary>
    /// Income and expenses whose lines do not sum to their amount. A leg of a transfer has no lines, so it is
    /// not one of them.
    /// </summary>
    private static IEnumerable<Drift> SplitsThatDrift(IReadOnlyCollection<Transaction> transactions) =>
        from transaction in transactions
        where !transaction.IsTransferLeg
        let linesTotal = transaction.Lines.Sum(line => line.Amount)
        where linesTotal != transaction.Amount
        select new Drift.Split(transaction.Id, transaction.Amount, linesTotal);

    /// <summary>The legs sharing each transfer id, against what <see cref="Transactions.Transfer"/> records.</summary>
    private static IEnumerable<Drift> TransfersThatDrift(IReadOnlyCollection<Transaction> transactions)
    {
        foreach (var legs in transactions.Where(transaction => transaction.TransferId is not null).GroupBy(transaction => transaction.TransferId!.Value))
        {
            if (ProblemWith([.. legs]) is { } problem)
            {
                yield return new Drift.Transfer(legs.Key, [.. legs.Select(leg => leg.Id)], problem);
            }
        }
    }

    private static TransferDrift? ProblemWith(IReadOnlyList<Transaction> legs) =>
        legs.Count != 2 ? TransferDrift.NotTwoLegs
        : legs[0].AccountId == legs[1].AccountId ? TransferDrift.OneAccount
        : legs[0].IsDeleted != legs[1].IsDeleted ? TransferDrift.OneLegDeleted
        : legs[0].Currency == legs[1].Currency && legs[0].Amount != legs[1].Amount ? TransferDrift.AmountsDiffer
        : legs[0].Date != legs[1].Date ? TransferDrift.DatesDiffer
        : null;

    /// <summary>
    /// Each account's balance worked out again here, one transaction at a time from its own
    /// <see cref="Transaction.SignedAmount"/>, against the balance the user is shown.
    /// </summary>
    private static IEnumerable<Drift> BalancesThatDrift(IReadOnlyCollection<Transaction> transactions, IReadOnlyDictionary<Guid, decimal> balances)
    {
        var fromTransactions = transactions
            .GroupBy(transaction => transaction.AccountId)
            .ToDictionary(account => account.Key, account => account.Sum(transaction => transaction.SignedAmount));

        foreach (var accountId in fromTransactions.Keys.Union(balances.Keys))
        {
            var (shown, added) = (balances.GetValueOrDefault(accountId), fromTransactions.GetValueOrDefault(accountId));
            if (shown != added)
            {
                yield return new Drift.Balance(accountId, shown, added);
            }
        }
    }
}

/// <summary>One thing in a user's saved transactions that does not add up, found by <see cref="TransactionIntegrity"/>.</summary>
public abstract record Drift
{
    private Drift()
    {
    }

    /// <summary>A transaction's lines do not sum to its amount (spec section 3, rule 5).</summary>
    /// <param name="LinesTotal">What its lines sum to: zero when it has none.</param>
    public sealed record Split(Guid TransactionId, decimal Amount, decimal LinesTotal) : Drift;

    /// <summary>The legs sharing a transfer id are not one Transfer out leg and one Transfer in leg that match.</summary>
    /// <param name="LegIds">Every transaction that carries the transfer id.</param>
    public sealed record Transfer(Guid TransferId, IReadOnlyList<Guid> LegIds, TransferDrift Problem) : Drift;

    /// <summary>
    /// The balance an account is shown with is not what its transactions come to when each is added up on its own.
    /// Both leave out the opening balance, which each adds the same way.
    /// </summary>
    /// <param name="Shown">What the account's transactions come to in the balance the user is shown.</param>
    /// <param name="FromTransactions">What they come to added up one at a time.</param>
    public sealed record Balance(Guid AccountId, decimal Shown, decimal FromTransactions) : Drift;
}

/// <summary>How the legs sharing a transfer id are not the two matching legs of one transfer.</summary>
public enum TransferDrift
{
    /// <summary>The transfer id is on one transaction, or on more than two.</summary>
    NotTwoLegs,

    /// <summary>One leg is deleted and the other is not, so the money left one account and never arrived, or the other way round.</summary>
    OneLegDeleted,

    /// <summary>Both legs are on one account, so nothing moved between two.</summary>
    OneAccount,

    /// <summary>The two accounts are in one currency, and the legs moved different amounts (spec section 3, rule 4).</summary>
    AmountsDiffer,

    /// <summary>The legs are on different dates, so for a while the money is on neither account or on both.</summary>
    DatesDiffer,
}
