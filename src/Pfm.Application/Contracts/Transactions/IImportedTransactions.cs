namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// What Imports asks of Transactions (spec IMP-1, IMP-3): the transactions an account already has, to find likely
/// duplicates among a statement's rows, and recording a statement's rows as transactions of an import batch.
/// </summary>
public interface IImportedTransactions
{
    /// <summary>The income and expenses on the user's account dated from <paramref name="from"/> to <paramref name="to"/>, both included; deleted ones left out.</summary>
    Task<IReadOnlyList<AccountTransaction>> ListOnAccountAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Records each as income or an expense on the account, in its currency, belonging to the import batch, by the
    /// rules of any transaction (converted into the base currency at the rate of its date). Nothing is saved: the
    /// caller saves them together with what it records of the batch, so both are saved or neither. Answers why they
    /// cannot be recorded (an account or a category archived since, say), with nothing recorded; null once they are.
    /// </summary>
    /// <param name="expenseCategoryId">The category an expense is recorded on.</param>
    /// <param name="incomeCategoryId">The category income is recorded on.</param>
    Task<string?> RecordAsync(
        Guid accountId, Guid importBatchId, IReadOnlyList<TransactionToImport> transactions, Guid? expenseCategoryId, Guid? incomeCategoryId,
        CancellationToken cancellationToken);
}

/// <summary>A transaction already on an account, as a likely duplicate is matched against it.</summary>
/// <param name="MoneyIn">Whether it brought money in: income, or a refund. A statement shows either as a credit.</param>
/// <param name="Amount">Greater than zero, in the account's currency.</param>
public sealed record AccountTransaction(DateOnly Date, bool MoneyIn, decimal Amount, string? Payee);

/// <summary>One statement row to record as a transaction.</summary>
/// <param name="IsIncome">Money came in, so it is income; else it is an expense.</param>
/// <param name="Amount">Greater than zero, in the account's currency.</param>
/// <param name="Payee">The row's description.</param>
public sealed record TransactionToImport(DateOnly Date, bool IsIncome, decimal Amount, string? Payee);
