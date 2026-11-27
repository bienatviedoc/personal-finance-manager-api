namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// What Imports asks of Transactions (spec IMP-1, IMP-3): the transactions an account already has, to find likely
/// duplicates among a statement's rows, and recording a statement's rows as transactions of an import batch.
/// </summary>
public interface IImportedTransactions
{
    /// <summary>
    /// The income, expenses and transfer legs on the user's account dated from <paramref name="from"/> to
    /// <paramref name="to"/>, both included; deleted ones left out.
    /// </summary>
    Task<IReadOnlyList<AccountTransaction>> ListOnAccountAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Records each as income or an expense on the account, in its currency, belonging to the import batch, by the
    /// rules of any transaction (converted into the base currency at the rate of its date). Nothing is saved: the
    /// caller saves them together with what it records of the batch, so both are saved or neither. Answers
    /// <see cref="RecordImportOutcome.Recorded"/>, or why they cannot be recorded, with nothing recorded.
    /// </summary>
    /// <param name="expenseCategoryId">The category an expense without one of its own is recorded on.</param>
    /// <param name="incomeCategoryId">The category income without one of its own is recorded on.</param>
    /// <param name="today">The user's today the statement was committed on: no transaction is dated after it.</param>
    Task<RecordImportOutcome> RecordAsync(
        Guid accountId, Guid importBatchId, IReadOnlyList<TransactionToImport> transactions, Guid? expenseCategoryId, Guid? incomeCategoryId,
        DateOnly today, CancellationToken cancellationToken);
}

/// <summary>What recording a statement's rows came to.</summary>
public enum RecordImportOutcome
{
    Recorded = 1,

    /// <summary>The account has been archived since the statement was committed.</summary>
    AccountArchived = 2,

    /// <summary>A category chosen for the statement has been archived since.</summary>
    CategoryArchived = 3,

    /// <summary>A row breaks another rule of transactions.</summary>
    TransactionRefused = 4,
}

/// <summary>A transaction already on an account, as a likely duplicate is matched against it.</summary>
/// <param name="MoneyIn">Whether it brought money in: income, a refund or a transfer in. A statement shows any of them as a credit.</param>
/// <param name="Amount">Greater than zero, in the account's currency.</param>
/// <param name="IsTransferLeg">One leg of a transfer, which has no payee.</param>
public sealed record AccountTransaction(DateOnly Date, bool MoneyIn, decimal Amount, string? Payee, bool IsTransferLeg);

/// <summary>One statement row to record as a transaction.</summary>
/// <param name="IsIncome">Money came in, so it is income; else it is an expense.</param>
/// <param name="Amount">Greater than zero, in the account's currency.</param>
/// <param name="Payee">The row's description.</param>
/// <param name="CategoryId">The category the user gave the row of its own; null for the batch's category of its kind.</param>
public sealed record TransactionToImport(DateOnly Date, bool IsIncome, decimal Amount, string? Payee, Guid? CategoryId = null);
