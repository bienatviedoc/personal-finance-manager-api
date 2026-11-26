using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>
/// The transactions a reconciliation matches against its statement: those ticked in it and, while it is in
/// progress, every other transaction on its account dated on or before the statement date that is in no
/// reconciliation yet. Oldest first, as a statement lists them. Not paged: a reconciliation is worked through
/// whole, and after the first one only what came since the last statement is left to tick.
/// </summary>
public sealed record ListReconciliationTransactions(Guid Id);

public sealed class ListReconciliationTransactionsHandler(IReconciliationRepository reconciliations, ITransactionRepository transactions) : IQueryHandler
{
    /// <summary>Null when the user has no reconciliation with that id.</summary>
    public async Task<IReadOnlyList<ReconciliationTransactionDetails>?> HandleAsync(ListReconciliationTransactions query, CancellationToken cancellationToken = default)
    {
        if (await reconciliations.FindAsync(query.Id, cancellationToken) is not { } reconciliation)
        {
            return null;
        }

        var listed = (await transactions.ListTickedAsync(reconciliation.Id, cancellationToken)).ToList();
        if (!reconciliation.IsCompleted)
        {
            listed.AddRange(await transactions.ListUnreconciledAsync(reconciliation.AccountId, reconciliation.StatementDate, cancellationToken));
        }

        return
        [
            .. listed
                .OrderBy(transaction => transaction.Date).ThenBy(transaction => transaction.CreatedAt).ThenBy(transaction => transaction.Id)
                .Select(transaction => Of(transaction, reconciliation)),
        ];
    }

    private static ReconciliationTransactionDetails Of(Transaction transaction, Reconciliation reconciliation) =>
        new(transaction.Id, transaction.Type, transaction.Date, transaction.Amount, transaction.Currency, transaction.IsRefund, transaction.Payee,
            transaction.Note, transaction.CategoryId, transaction.TransferId, transaction.ReconciliationId == reconciliation.Id);
}
