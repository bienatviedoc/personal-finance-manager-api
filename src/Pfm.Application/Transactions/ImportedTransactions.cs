using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Transactions.Categories;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Transactions as Imports reads and records them. An imported transaction is recorded by the same rules as one a
/// user records (<see cref="Transaction.Record"/>), on a category the user chose for the batch, with the row's
/// description as its payee.
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

    public async Task<string?> RecordAsync(
        Guid accountId, Guid importBatchId, IReadOnlyList<TransactionToImport> toImport, Guid? expenseCategoryId, Guid? incomeCategoryId,
        DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toImport);
        if (await accounts.FindChoosableAsync(accountId, keeps: null, cancellationToken) is not { } account)
        {
            return "The account has been archived since the statement was committed.";
        }

        var expenseCategory = expenseCategoryId is { } expenseId ? await categories.FindAsync(expenseId, cancellationToken) : null;
        var incomeCategory = incomeCategoryId is { } incomeId ? await categories.FindAsync(incomeId, cancellationToken) : null;
        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var recorded = new List<Transaction>(toImport.Count);
        try
        {
            foreach (var row in toImport)
            {
                var category = (row.IsIncome ? incomeCategory : expenseCategory)
                    ?? throw new TransactionRuleViolation(nameof(Transaction.CategoryId), "The category chosen for the statement has been deleted.");
                var transaction = Transaction.Record(
                    row.IsIncome ? TransactionType.Income : TransactionType.Expense, account.Id, account.Currency, row.Amount, row.Date, category, row.Payee,
                    note: null, today, now);
                transaction.ImportedBy(importBatchId);
                recorded.Add(transaction);
            }
        }
        catch (TransactionRuleViolation violation)
        {
            return violation.Message;
        }

        foreach (var transaction in recorded)
        {
            await conversion.ConvertAsync(transaction, currency, cancellationToken);
            transactions.Add(transaction, request: null);
        }

        return null;
    }
}
