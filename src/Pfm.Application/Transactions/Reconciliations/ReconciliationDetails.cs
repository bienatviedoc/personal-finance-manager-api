using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>A reconciliation as the use cases return it, with what is ticked worked out each time.</summary>
/// <param name="Currency">The account's, which the balances are in.</param>
/// <param name="ReconciledBalance">The account's opening balance plus every transaction reconciled in its other reconciliations.</param>
/// <param name="TickedTotal">What the transactions ticked in this one do to the balance.</param>
/// <param name="Difference">The statement balance less the reconciled balance and the ticked total: zero when it can complete.</param>
public sealed record ReconciliationDetails(
    Guid Id, Guid AccountId, Currency Currency, DateOnly StatementDate, decimal StatementBalance, ReconciliationStatus Status, DateTimeOffset? CompletedAt,
    decimal ReconciledBalance, decimal TickedTotal, decimal Difference, int TickedCount)
{
    public static ReconciliationDetails Of(Reconciliation reconciliation, Currency currency, decimal reconciledBalance, IReadOnlyCollection<Transaction> ticked) =>
        new(reconciliation.Id, reconciliation.AccountId, currency, reconciliation.StatementDate, reconciliation.StatementBalance, reconciliation.Status,
            reconciliation.CompletedAt, reconciledBalance, reconciliation.TickedTotal(ticked), reconciliation.DifferenceWith(reconciledBalance, ticked),
            ticked.Count(transaction => transaction.ReconciliationId == reconciliation.Id && !transaction.IsDeleted));
}

/// <summary>A transaction as a reconciliation lists it to tick.</summary>
/// <param name="IsTicked">Whether it is ticked in this reconciliation.</param>
public sealed record ReconciliationTransactionDetails(
    Guid Id, TransactionType Type, DateOnly Date, decimal Amount, Currency Currency, bool IsRefund, string? Payee, string? Note, Guid? CategoryId,
    Guid? TransferId, bool IsTicked);
