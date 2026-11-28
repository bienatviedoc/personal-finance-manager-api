using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Transactions as Imports reads and records them. An imported transaction is recorded by the same rules as one a
/// user records (<see cref="Transaction.Record"/>), on the category the user gave the row, or else the one they chose
/// for the batch, with the row's description as its payee.
/// </summary>
public sealed class ImportedTransactions(
    ITransactionRepository transactions, IAccountLookup accounts, ICategoryRepository categories, IBaseCurrency baseCurrency,
    TransactionConversion conversion, TimeProvider clock)
    : IImportedTransactions
{
    public async Task<IReadOnlyList<AccountTransaction>> ListOnAccountAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        [.. (await transactions.ListOnAccountAsync(accountId, from, to, cancellationToken))
            .Select(transaction => new AccountTransaction(
                transaction.Date, transaction.SignedAmount > 0, transaction.Amount, transaction.Payee, !transaction.Type.IsIncomeOrExpense()))];

    public async Task<RecordImportOutcome> RecordAsync(
        Guid accountId, Guid importBatchId, IReadOnlyList<TransactionToImport> toImport, Guid? expenseCategoryId, Guid? incomeCategoryId,
        DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toImport);
        if (await accounts.FindChoosableAsync(accountId, keeps: null, cancellationToken) is not { } account)
        {
            return RecordImportOutcome.AccountArchived;
        }

        var expenseCategory = expenseCategoryId is { } expenseId ? await categories.FindAsync(expenseId, cancellationToken) : null;
        var incomeCategory = incomeCategoryId is { } incomeId ? await categories.FindAsync(incomeId, cancellationToken) : null;
        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        var own = new Dictionary<Guid, Category?>();
        foreach (var categoryId in toImport.Select(row => row.CategoryId).OfType<Guid>().Distinct())
        {
            own[categoryId] = await categories.FindAsync(categoryId, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var recorded = new List<Transaction>(toImport.Count);
        try
        {
            foreach (var row in toImport)
            {
                var category = (row.CategoryId is { } ownId ? own[ownId] : row.IsIncome ? incomeCategory : expenseCategory)
                    ?? throw new TransactionRuleViolation(nameof(Transaction.CategoryId), "The category chosen for the statement is gone.");
                var transaction = Transaction.Record(
                    row.IsIncome ? TransactionType.Income : TransactionType.Expense, new AccountAmount(account.Id, account.Currency, row.Amount), row.Date, category, row.Payee,
                    note: null, new Moment(today, now));
                transaction.ImportedBy(importBatchId);
                recorded.Add(transaction);
            }
        }
        catch (TransactionRuleViolation violation)
        {
            return violation.Property == nameof(Transaction.CategoryId) ? RecordImportOutcome.CategoryArchived : RecordImportOutcome.TransactionRefused;
        }

        foreach (var transaction in recorded)
        {
            await conversion.ConvertAsync(transaction, currency, cancellationToken);
            transactions.Add(transaction, request: null);
        }

        return RecordImportOutcome.Recorded;
    }

    public async Task<DeleteImportedOutcome> DeleteAsync(Guid importBatchId, CancellationToken cancellationToken)
    {
        var imported = await transactions.ListImportedByAsync(importBatchId, cancellationToken);
        var transfers = new Dictionary<Guid, Transfer>();
        foreach (var transferId in imported.Select(transaction => transaction.TransferId).OfType<Guid>().Distinct())
        {
            // A transaction that is not deleted names a transfer whose legs are not deleted either: they are deleted together.
            transfers[transferId] = await transactions.FindTransferAsync(transferId, deletedToo: false, cancellationToken)
                ?? throw new InvalidOperationException($"Import batch {importBatchId} has a leg of transfer {transferId}, which was not found: its legs are out of step.");
        }

        var reconciled = imported.Count(transaction => transaction.TransferId is { } transferId ? transfers[transferId].IsReconciled : transaction.IsReconciled);
        if (reconciled > 0)
        {
            return new DeleteImportedOutcome.Reconciled(reconciled);
        }

        var now = clock.GetUtcNow();
        foreach (var transfer in transfers.Values)
        {
            transfer.Delete(now);
        }

        var incomeAndExpenses = imported.Where(transaction => transaction.TransferId is null).ToList();
        foreach (var transaction in incomeAndExpenses)
        {
            transaction.Delete(now);
        }

        return new DeleteImportedOutcome.Deleted(incomeAndExpenses.Count + (2 * transfers.Count), [.. incomeAndExpenses.Select(transaction => transaction.Date).Distinct()]);
    }
}
