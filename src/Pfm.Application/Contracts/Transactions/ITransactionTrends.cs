using Pfm.Domain.Transactions;

namespace Pfm.Application.Contracts.Transactions;

/// <summary>What other modules may ask about one category or one payee over time (spec RPT-7).</summary>
public interface ITransactionTrends
{
    /// <summary>
    /// What each of the user's transactions dated from <paramref name="from"/> to <paramref name="to"/> adds to a trend,
    /// in the base currency, each as it was converted at the exchange rate of its own date (decision record 0002).
    /// Either the expenses, each adding what it spent (a refund taking its amount away), or the income, each adding
    /// what it received. A split adds only its lines on <paramref name="categoryIds"/>. Transfers and deleted
    /// transactions never count.
    /// </summary>
    /// <param name="income">The income when true; the expenses when false.</param>
    /// <param name="categoryIds">Only the lines on these categories; every line when null.</param>
    /// <param name="payee">Only the transactions with this payee, trimmed, in any case; any payee, or none, when null.</param>
    /// <param name="accountId">Only the transactions on this account; on every account when null.</param>
    Task<IReadOnlyList<TrendTransaction>> AmountsAsync(
        DateOnly from, DateOnly to, bool income, IReadOnlyCollection<Guid>? categoryIds, string? payee, Guid? accountId, CancellationToken cancellationToken);
}

/// <summary>What one transaction adds to a trend.</summary>
/// <param name="Amount">In the base currency; null while it is not converted, when it adds nothing.</param>
public sealed record TrendTransaction(Guid TransactionId, DateOnly Date, decimal? Amount);

/// <summary>What another module needs to know of a payee.</summary>
public static class Payees
{
    /// <summary>The most characters a transaction's payee has, so a longer one matches none.</summary>
    public const int MaxLength = Transaction.MaxPayeeLength;
}
