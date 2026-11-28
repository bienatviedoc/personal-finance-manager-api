namespace Pfm.Domain.Transactions;

/// <summary>
/// A movement of a user's own money between two of their accounts: two linked transactions sharing a transfer
/// id, a Transfer out leg on the source account and a Transfer in leg on the destination (spec section 3,
/// rule 3; decision record 0001). It is neither income nor spending, so its legs have no category. The legs
/// are recorded, edited and deleted together, here and nowhere else.
/// </summary>
/// <remarks>
/// Each leg holds its own amount in its own account's currency. Between accounts in one currency the two
/// amounts are the same; between currencies the user enters both, and the exchange rate is what the two
/// amounts imply, never looked up (rule 4).
/// </remarks>
public sealed class Transfer
{
    private Transfer(Transaction outLeg, Transaction inLeg)
    {
        OutLeg = outLeg;
        InLeg = inLeg;
    }

    /// <summary>The transfer id the two legs share.</summary>
    public Guid Id => OutLeg.TransferId!.Value;

    /// <summary>The Transfer out leg, on the source account.</summary>
    public Transaction OutLeg { get; }

    /// <summary>The Transfer in leg, on the destination account.</summary>
    public Transaction InLeg { get; }

    /// <summary>The source account, which the money leaves.</summary>
    public Guid FromAccountId => OutLeg.AccountId;

    /// <summary>The destination account, which the money arrives on.</summary>
    public Guid ToAccountId => InLeg.AccountId;

    /// <summary>What leaves the source account, in its currency.</summary>
    public decimal Amount => OutLeg.Amount;

    /// <summary>What arrives on the destination account, in its currency.</summary>
    public decimal ToAmount => InLeg.Amount;

    public DateOnly Date => OutLeg.Date;

    public string? Note => OutLeg.Note;

    public bool IsDeleted => OutLeg.IsDeleted;

    /// <summary>
    /// Whether either leg is reconciled, which locks the whole transfer (spec section 3, rule 3): moving one leg
    /// would move the other, and the reconciled one cannot move.
    /// </summary>
    public bool IsReconciled => OutLeg.IsReconciled || InLeg.IsReconciled;

    /// <summary>
    /// The version an edit of the transfer names: the Transfer out leg's. It stands for both legs because the user
    /// changes a leg only through this type, which changes both together (a leg alone is refused), so an edit
    /// moves both versions at once. What changes one leg alone, converting it into the base currency once a rate
    /// arrives, is no edit to call stale. The in leg's own version still guards the save: one changed in between
    /// fails it as a concurrent change.
    /// </summary>
    public uint RowVersion => OutLeg.RowVersion;

    /// <param name="from">What leaves the source account.</param>
    /// <param name="to">What arrives on the destination account.</param>
    public static Transfer Record(AccountAmount from, AccountAmount to, DateOnly date, string? note, Moment at)
    {
        ArgumentNullException.ThrowIfNull(at);
        GuardCanMove(from, to);
        var id = Guid.CreateVersion7(at.Now);
        var outLeg = OfLeg(nameof(Amount), () => Transaction.RecordLeg(id, TransactionType.TransferOut, from, date, note, at));
        var inLeg = OfLeg(nameof(ToAmount), () => Transaction.RecordLeg(id, TransactionType.TransferIn, to, date, note, at));
        return new Transfer(outLeg, inLeg);
    }

    /// <summary>What a broken rule names the expense a transfer is marked from by: the id it is picked by.</summary>
    public const string ExpenseId = nameof(ExpenseId);

    /// <summary>What a broken rule names the income a transfer is marked from by: the id it is picked by.</summary>
    public const string IncomeId = nameof(IncomeId);

    /// <summary>What a broken rule names the category the Transfer out leg is spent on once unmarked by: the id it is chosen by.</summary>
    public const string ExpenseCategoryId = nameof(ExpenseCategoryId);

    /// <summary>What a broken rule names the category the Transfer in leg is income on once unmarked by: the id it is chosen by.</summary>
    public const string IncomeCategoryId = nameof(IncomeCategoryId);

    /// <summary>
    /// Joins an expense on one account and income on another, such as the two rows imported statements show
    /// for paying a card, into the two legs of one transfer (spec IMP-7; decision record 0001): the expense
    /// becomes the Transfer out leg and the income the Transfer in leg. Each keeps its id, account, amount,
    /// date, note and conversion; it loses its category, lines and payee, as a leg has none, so it leaves
    /// income, expense and budget totals. Neither changes when a rule is broken.
    /// </summary>
    /// <exception cref="InvalidOperationException">Either transaction is deleted.</exception>
    public static Transfer Mark(Transaction expense, Transaction income, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(income);
        if (expense.IsDeleted || income.IsDeleted)
        {
            throw new InvalidOperationException("A deleted transaction cannot be marked as a transfer.");
        }

        GuardCanBecomeLeg(expense, ExpenseId, TransactionType.Expense, "Choose an expense that is not a refund: the money that left one account.");
        GuardCanBecomeLeg(income, IncomeId, TransactionType.Income, "Choose income: the money that arrived on the other account.");
        if (expense.AccountId == income.AccountId)
        {
            throw new TransferRuleViolation(IncomeId, "A transfer is between two different accounts.");
        }

        if (expense.Currency == income.Currency && expense.Amount != income.Amount)
        {
            throw new TransferRuleViolation(IncomeId, "Accounts in the same currency move the same amount.");
        }

        if (expense.Date != income.Date)
        {
            throw new TransferRuleViolation(IncomeId, "The two legs of a transfer are on the same date.");
        }

        var id = Guid.CreateVersion7(now);
        expense.BecomeLeg(id, TransactionType.TransferOut, now);
        income.BecomeLeg(id, TransactionType.TransferIn, now);
        return new Transfer(expense, income);
    }

    /// <summary>
    /// Turns the transfer back into two ordinary transactions, each keeping its id, account, amount, date and
    /// note: the Transfer out leg becomes an expense on <paramref name="expenseCategory"/> and the Transfer in
    /// leg income on <paramref name="incomeCategory"/>. Any transfer can be, marked or recorded. Neither leg
    /// changes when a rule is broken; once unmarked, this transfer is no more.
    /// </summary>
    /// <exception cref="InvalidOperationException">The transfer is deleted.</exception>
    /// <exception cref="TransferRuleViolation">Either leg is reconciled, or a category is not one a leg can take.</exception>
    public (Transaction Expense, Transaction Income) Unmark(Category expenseCategory, Category incomeCategory, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(expenseCategory);
        ArgumentNullException.ThrowIfNull(incomeCategory);
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted transfer cannot be unmarked.");
        }

        if (IsReconciled)
        {
            throw new TransferRuleViolation(nameof(Id), "This transfer is reconciled. Reopen its reconciliation to unmark it.");
        }

        if (Transaction.CategoryRuleBroken(expenseCategory, TransactionType.Expense, keeps: false) is { } expenseRule)
        {
            throw new TransferRuleViolation(ExpenseCategoryId, expenseRule);
        }

        if (Transaction.CategoryRuleBroken(incomeCategory, TransactionType.Income, keeps: false) is { } incomeRule)
        {
            throw new TransferRuleViolation(IncomeCategoryId, incomeRule);
        }

        OutLeg.BecomeOrdinary(TransactionType.Expense, expenseCategory, now);
        InLeg.BecomeOrdinary(TransactionType.Income, incomeCategory, now);
        return (OutLeg, InLeg);
    }

    /// <summary>Pairs the two legs of one transfer, as read back, in either order.</summary>
    /// <exception cref="ArgumentException">The transactions are not one Transfer out leg and one Transfer in leg sharing a transfer id.</exception>
    public static Transfer Of(IReadOnlyCollection<Transaction> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        var outLeg = legs.FirstOrDefault(leg => leg.Type == TransactionType.TransferOut);
        var inLeg = legs.FirstOrDefault(leg => leg.Type == TransactionType.TransferIn);
        if (legs.Count != 2 || outLeg?.TransferId is null || inLeg is null || inLeg.TransferId != outLeg.TransferId)
        {
            throw new ArgumentException("A transfer is one Transfer out leg and one Transfer in leg sharing a transfer id.", nameof(legs));
        }

        return new Transfer(outLeg, inLeg);
    }

    /// <summary>Changes both legs together, to other accounts if need be; neither changes when a rule is broken.</summary>
    /// <exception cref="InvalidOperationException">The transfer is deleted.</exception>
    public void Edit(AccountAmount from, AccountAmount to, DateOnly date, string? note, Moment at)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted transfer cannot be edited.");
        }

        GuardCanMove(from, to);
        GuardUnlocked(from, to, date);

        // Checked on a leg nobody keeps first, so the out leg is not left changed when the in leg is refused.
        OfLeg(nameof(ToAmount), () => Transaction.RecordLeg(Id, TransactionType.TransferIn, to, date, note, at));
        OfLeg(nameof(Amount), () => OutLeg.EditLeg(from, date, note, at));
        OfLeg(nameof(ToAmount), () => InLeg.EditLeg(to, date, note, at));
    }

    /// <summary>Deletes both legs together, so both accounts' balances are as before the transfer. Deleting it again changes nothing.</summary>
    /// <exception cref="TransactionRuleViolation">Either leg is reconciled.</exception>
    public void Delete(DateTimeOffset now)
    {
        if (IsReconciled && !IsDeleted)
        {
            throw new TransactionRuleViolation(
                nameof(IsReconciled), "This transfer is reconciled: reopen its reconciliation to delete it.", BulkRefusal.Reconciled);
        }

        OutLeg.DeleteLeg(now);
        InLeg.DeleteLeg(now);
    }

    /// <summary>Refuses, while either leg is reconciled, a change to the accounts, the amounts or the date.</summary>
    private void GuardUnlocked(AccountAmount from, AccountAmount to, DateOnly date)
    {
        if (!IsReconciled)
        {
            return;
        }

        var changed =
            from.AccountId != FromAccountId ? nameof(FromAccountId)
            : to.AccountId != ToAccountId ? nameof(ToAccountId)
            : from.Amount != Amount ? nameof(Amount)
            : to.Amount != ToAmount ? nameof(ToAmount)
            : date != Date ? nameof(Date)
            : null;
        if (changed is not null)
        {
            throw new TransferRuleViolation(
                changed, "This transfer is reconciled, so its accounts, amounts and date cannot change until its reconciliation is reopened.");
        }
    }

    /// <summary>Restores both legs together, so both accounts' balances move again. Restoring one that is not deleted changes nothing.</summary>
    /// <exception cref="InvalidOperationException">The transfer was deleted longer than <see cref="Transaction.RecoveryPeriod"/> ago.</exception>
    public void Restore(DateTimeOffset now)
    {
        // Checked on both before either changes, so one leg is never restored alone.
        if (IsDeleted && (!OutLeg.IsRecoverableAt(now) || !InLeg.IsRecoverableAt(now)))
        {
            throw new InvalidOperationException($"A transfer deleted more than {Transaction.RecoveryPeriod.TotalDays} days ago cannot be restored.");
        }

        OutLeg.RestoreLeg(now);
        InLeg.RestoreLeg(now);
    }

    private static void GuardCanBecomeLeg(Transaction transaction, string property, TransactionType type, string notThatType)
    {
        if (transaction.IsTransferLeg)
        {
            throw new TransferRuleViolation(property, "This is a leg of a transfer already.");
        }

        if (transaction.IsReconciled)
        {
            throw new TransferRuleViolation(property, "This transaction is reconciled. Reopen its reconciliation to mark it as a transfer.");
        }

        if (transaction.Type != type || transaction.IsRefund)
        {
            throw new TransferRuleViolation(property, notThatType);
        }
    }

    private static void GuardCanMove(AccountAmount from, AccountAmount to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.AccountId == to.AccountId)
        {
            throw new TransferRuleViolation(nameof(ToAccountId), "A transfer is between two different accounts.");
        }

        if (from.Currency == to.Currency && from.Amount != to.Amount)
        {
            throw new TransferRuleViolation(nameof(ToAmount), "Accounts in the same currency move the same amount.");
        }
    }

    /// <summary>A leg is held to the rules of any transaction; one it breaks is named as the transfer names it.</summary>
    /// <param name="amountProperty">What the transfer calls this leg's amount.</param>
    private static Transaction OfLeg(string amountProperty, Func<Transaction> record)
    {
        try
        {
            return record();
        }
        catch (TransactionRuleViolation violation)
        {
            throw new TransferRuleViolation(
                violation.Property == nameof(Transaction.Amount) ? amountProperty : violation.Property,
                violation.Message.Replace("A transaction's date", "A transfer's date", StringComparison.Ordinal));
        }
    }

    private static void OfLeg(string amountProperty, Action edit) =>
        OfLeg(amountProperty, () =>
        {
            edit();
            return (Transaction)null!;
        });
}
