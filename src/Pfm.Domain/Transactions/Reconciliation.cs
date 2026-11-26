namespace Pfm.Domain.Transactions;

/// <summary>Where a reconciliation stands: being ticked, or completed, which locks its transactions.</summary>
public enum ReconciliationStatus
{
    InProgress,
    Completed,
}

/// <summary>
/// Matching an account's transactions against a statement's closing balance (spec ACC-7). The user ticks the
/// transactions the statement shows until the difference between the statement balance and what is ticked is
/// exactly zero, then completes it, which locks those transactions: their amount, date and account cannot change
/// until it is reopened (spec section 3, rule 11). Nothing is ever added to make the difference zero.
/// </summary>
/// <remarks>
/// What is ticked is measured from the account's reconciled balance: its opening balance and every transaction
/// already reconciled in another reconciliation of the account. So a statement's closing balance, which carries
/// everything before it, is matched without ticking the account's whole history again each month.
/// </remarks>
public sealed class Reconciliation : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private Reconciliation()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The account being matched against its statement.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The closing date of the statement: transactions dated after it are not on it.</summary>
    public DateOnly StatementDate { get; private set; }

    /// <summary>The statement's closing balance, in the account's currency: below zero for what is owed on a credit card or loan.</summary>
    public decimal StatementBalance { get; private set; }

    /// <summary>When it was completed, locking its transactions; null while it is in progress.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Changes every time the reconciliation is saved, ticking included, so completing it cannot miss a
    /// transaction ticked at the same moment.
    /// </summary>
    public uint RowVersion { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public ReconciliationStatus Status => IsCompleted ? ReconciliationStatus.Completed : ReconciliationStatus.InProgress;

    /// <param name="currency">The currency of the account, which the statement balance is in.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public static Reconciliation Start(Guid accountId, Currency currency, DateOnly statementDate, decimal statementBalance, DateOnly today, DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A reconciliation is of an account.", nameof(accountId));
        }

        if (statementDate > today)
        {
            throw new ReconciliationRuleViolation(nameof(StatementDate), "A statement date cannot be in the future.");
        }

        if (!Amounts.IsValid(statementBalance, currency))
        {
            throw new ReconciliationRuleViolation(
                nameof(StatementBalance),
                $"A balance in {currency} has at most {currency.DecimalPlaces()} decimals and {Amounts.MaxIntegerDigits} digits before them.");
        }

        return new Reconciliation
        {
            Id = Guid.CreateVersion7(now),
            AccountId = accountId,
            StatementDate = statementDate,
            StatementBalance = statementBalance,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>What the transactions ticked in this reconciliation do to the account's balance; a deleted one does nothing.</summary>
    public decimal TickedTotal(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        return transactions.Where(transaction => transaction.ReconciliationId == Id).Sum(transaction => transaction.SignedAmount);
    }

    /// <summary>
    /// How far the statement balance is from what is ticked, measured from the account's reconciled balance:
    /// zero when they match, and only then can the reconciliation complete.
    /// </summary>
    /// <param name="reconciledBalance">The account's opening balance plus every transaction reconciled in another of its reconciliations.</param>
    public decimal DifferenceWith(decimal reconciledBalance, IEnumerable<Transaction> transactions) =>
        StatementBalance - reconciledBalance - TickedTotal(transactions);

    /// <summary>Ticks a transaction the statement shows. Ticking it again changes nothing.</summary>
    /// <exception cref="ReconciliationRuleViolation">
    /// The reconciliation is completed, or the transaction is not one it can match: on another account, dated
    /// after the statement, deleted, or already in another reconciliation.
    /// </exception>
    public void Tick(Transaction transaction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        GuardInProgress();
        if (transaction.ReconciliationId == Id)
        {
            return;
        }

        var refused =
            transaction.AccountId != AccountId ? "Only a transaction on the account being reconciled can be ticked."
            : transaction.IsDeleted ? "A deleted transaction cannot be ticked."
            : transaction.Date > StatementDate ? "A transaction dated after the statement date is not on the statement."
            : transaction.ReconciliationId is not null ? "This transaction is already in another reconciliation."
            : null;
        if (refused is not null)
        {
            throw new ReconciliationRuleViolation(TransactionId, refused);
        }

        transaction.TickIn(Id);
        UpdatedAt = now;
    }

    /// <summary>Unticks a transaction. Unticking one that is not ticked here changes nothing.</summary>
    /// <exception cref="ReconciliationRuleViolation">The reconciliation is completed.</exception>
    public void Untick(Transaction transaction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        GuardInProgress();
        if (transaction.ReconciliationId != Id)
        {
            return;
        }

        transaction.Untick();
        UpdatedAt = now;
    }

    /// <summary>Completes the reconciliation and locks every transaction ticked in it.</summary>
    /// <param name="reconciledBalance">The account's opening balance plus every transaction reconciled in another of its reconciliations.</param>
    /// <param name="ticked">Every transaction ticked in it; any other is passed over.</param>
    /// <exception cref="ReconciliationRuleViolation">The difference is not exactly zero, or it is already completed.</exception>
    public void Complete(decimal reconciledBalance, IReadOnlyCollection<Transaction> ticked, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        GuardInProgress();
        var difference = DifferenceWith(reconciledBalance, ticked);
        if (difference != 0)
        {
            throw new ReconciliationRuleViolation(Difference, $"A reconciliation completes only when the difference is exactly zero; it is {difference}.");
        }

        foreach (var transaction in ticked.Where(transaction => transaction.ReconciliationId == Id && !transaction.IsDeleted))
        {
            transaction.Reconcile();
        }

        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Reopens a completed reconciliation: its transactions are unlocked and stay ticked. Reopening one in progress changes nothing.</summary>
    /// <param name="ticked">Every transaction ticked in it; any other is passed over.</param>
    public void Reopen(IReadOnlyCollection<Transaction> ticked, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        if (!IsCompleted)
        {
            return;
        }

        foreach (var transaction in ticked.Where(transaction => transaction.ReconciliationId == Id))
        {
            transaction.Unreconcile();
        }

        CompletedAt = null;
        UpdatedAt = now;
    }

    /// <summary>What a tick names as at fault when a transaction cannot be ticked.</summary>
    public const string TransactionId = "TransactionId";

    /// <summary>What completing names as at fault when the difference is not zero.</summary>
    public const string Difference = "Difference";

    private void GuardInProgress()
    {
        if (IsCompleted)
        {
            throw new ReconciliationRuleViolation(nameof(CompletedAt), "This reconciliation is completed. Reopen it to change what is ticked.");
        }
    }
}
