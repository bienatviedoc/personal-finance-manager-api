namespace Pfm.Domain.Transactions;

/// <summary>
/// A template that produces income or expense transactions on a schedule, each dated on its due date (see the
/// glossary). It holds what each transaction will say and when the next one is due; the worker posts it.
/// Recurring rules cover income and expenses only, never transfers (spec section 5).
/// </summary>
public sealed class RecurringRule : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private RecurringRule()
    {
    }

    private RecurringRule(DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Income or an expense.</summary>
    public TransactionType Type { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Always greater than zero, in the currency of the account; <see cref="Type"/> gives the sign.</summary>
    public decimal Amount { get; private set; }

    public Guid CategoryId { get; private set; }

    public string? Payee { get; private set; }

    public string? Note { get; private set; }

    public RecurringFrequency Frequency { get; private set; }

    /// <summary>The N of an every N days rule; 1 for every other frequency.</summary>
    public int Interval { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    /// <summary>
    /// The due date of the next occurrence to post; null once the rule has ended. A date in the past is an
    /// occurrence that was missed, and is posted with that date (spec section 5).
    /// </summary>
    public DateOnly? NextRunDate { get; private set; }

    /// <summary>
    /// The date up to which every occurrence is done with: posted, or skipped while the rule was paused. Null
    /// until the first is. An edit of the schedule carries on after it, so nothing is posted twice.
    /// </summary>
    public DateOnly? SettledThrough { get; private set; }

    /// <summary>A paused rule posts nothing until it is resumed.</summary>
    public bool IsPaused { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes every time the rule is saved, so two posts of the same occurrence cannot both move it on.</summary>
    public uint RowVersion { get; private set; }

    public RecurringSchedule Schedule => RecurringSchedule.Of(Frequency, Frequency == RecurringFrequency.EveryNDays ? Interval : null, StartDate, EndDate);

    /// <param name="currency">The currency of the account, which the amount must fit.</param>
    /// <param name="category">One of the user's categories that is not archived, of the kind the type takes.</param>
    public static RecurringRule Create(
        TransactionType type, Guid accountId, Currency currency, decimal amount, Category category, string? payee, string? note,
        RecurringSchedule schedule, DateTimeOffset now)
    {
        var rule = new RecurringRule(now);
        rule.Set(type, accountId, currency, amount, category, payee, note, schedule, now);
        return rule;
    }

    /// <summary>
    /// Changes what the rule's next transactions say and when they are due. Transactions already posted stay
    /// as they are, and no occurrence on or before the last one posted or skipped is posted by the new schedule.
    /// The rule may keep a category that has since been archived, but not move to one.
    /// </summary>
    public void Edit(
        TransactionType type, Guid accountId, Currency currency, decimal amount, Category category, string? payee, string? note,
        RecurringSchedule schedule, DateTimeOffset now) =>
        Set(type, accountId, currency, amount, category, payee, note, schedule, now);

    /// <summary>The occurrence due on <paramref name="dueDate"/> has its transaction: the rule moves on to the next.</summary>
    /// <exception cref="InvalidOperationException">That occurrence is not the one due next.</exception>
    public void Posted(DateOnly dueDate, DateTimeOffset now)
    {
        if (dueDate != NextRunDate)
        {
            throw new InvalidOperationException("Only the occurrence that is due next can be posted.");
        }

        SettleThrough(dueDate, now);
    }

    public void Pause(DateTimeOffset now)
    {
        IsPaused = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Has the rule post again, from today on: the occurrences that were due while it was paused are skipped,
    /// not posted late. Resuming a rule that is not paused changes nothing.
    /// </summary>
    /// <param name="today">Today's date in the user's time zone.</param>
    public void Resume(DateOnly today, DateTimeOffset now)
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        var yesterday = today.AddDays(-1);
        SettleThrough(SettledThrough > yesterday ? SettledThrough.Value : yesterday, now);
    }

    private void SettleThrough(DateOnly date, DateTimeOffset now)
    {
        SettledThrough = date;
        NextRunDate = Schedule.FirstAfter(date);
        UpdatedAt = now;
    }

    private void Set(
        TransactionType type, Guid accountId, Currency currency, decimal amount, Category category, string? payee, string? note,
        RecurringSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A recurring rule is on an account.", nameof(accountId));
        }

        if (!Transaction.IsValidPayee(payee))
        {
            throw new ArgumentException($"A payee is at most {Transaction.MaxPayeeLength} characters.", nameof(payee));
        }

        if (!Transaction.IsValidNote(note))
        {
            throw new ArgumentException($"A note is at most {Transaction.MaxNoteLength} characters.", nameof(note));
        }

        if (!type.IsIncomeOrExpense())
        {
            throw new RecurringRuleViolation(nameof(Type), "A recurring rule is income or an expense.");
        }

        // Held to the rules of the transactions it posts.
        if (Transaction.AmountRuleBroken(amount, currency) is { } amountRule)
        {
            throw new RecurringRuleViolation(nameof(Amount), amountRule);
        }

        if (Transaction.CategoryRuleBroken(category, type, keeps: category.Id == CategoryId) is { } categoryRule)
        {
            throw new RecurringRuleViolation(nameof(CategoryId), categoryRule);
        }

        Type = type;
        AccountId = accountId;
        Amount = amount;
        CategoryId = category.Id;
        Payee = Transaction.Normalised(payee);
        Note = Transaction.Normalised(note);
        Frequency = schedule.Frequency;
        Interval = schedule.Interval;
        StartDate = schedule.StartDate;
        EndDate = schedule.EndDate;
        NextRunDate = schedule.FirstAfter(SettledThrough);
        UpdatedAt = now;
    }
}
