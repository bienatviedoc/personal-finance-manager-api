using Pfm.Application.Contracts.Accounts;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>
/// Works out what a reconciliation's numbers are: its account's reconciled balance and what is ticked in it.
/// Shared by every reconciliation use case.
/// </summary>
public sealed class ReconciliationContext(IAccountLookup accounts, ITransactionRepository transactions)
{
    /// <summary>The account's opening balance plus every transaction reconciled in its other reconciliations, and its currency.</summary>
    internal async Task<(AccountSummary Account, decimal ReconciledBalance)> ReconciledBalanceAsync(Reconciliation reconciliation, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(reconciliation.AccountId, cancellationToken)
            ?? throw new InvalidOperationException("A reconciliation's account is one of its user's.");
        return (account, account.OpeningBalance + await transactions.ReconciledNetAsync(reconciliation.AccountId, reconciliation.Id, cancellationToken));
    }

    /// <summary>The reconciliation as saved, with its difference.</summary>
    public async Task<ReconciliationDetails> DetailsAsync(Reconciliation reconciliation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        var (account, reconciledBalance) = await ReconciledBalanceAsync(reconciliation, cancellationToken);
        var ticked = await transactions.ListTickedAsync(reconciliation.Id, cancellationToken);
        return ReconciliationDetails.Of(reconciliation, account.Currency, reconciledBalance, ticked);
    }
}
