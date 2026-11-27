using System.Globalization;

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

    /// <summary>Whether the worker posts an occurrence on its due date, or it becomes a reminder the user confirms or dismisses.</summary>
    public RecurringMode Mode { get; private set; }

    /// <summary>A paused rule posts nothing until it is resumed.</summary>
    public bool IsPaused { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes every time the rule is saved, so two posts of the same occurrence cannot both move it on.</summary>
    public uint RowVersion { get; private set; }

    public RecurringSchedule Schedule => RecurringSchedule.Of(Frequency, Frequency == RecurringFrequency.EveryNDays ? Interval : null, StartDate, EndDate);

    /// <param name="currency">The currency of the account, which the amount must fit.</param>
    /// <param name="category">One of the user's categories that is not archived, of the kind the type takes.</param>
    /// <param name="mode">Whether the worker posts each occurrence, or each becomes a reminder.</param>
    public static RecurringRule Create(
        TransactionType type, Guid accountId, Currency currency, decimal amount, Category category, string? payee, string? note,
        RecurringSchedule schedule, DateTimeOffset now, RecurringMode mode = RecurringMode.AutoPost)
    {
        var rule = new RecurringRule(now);
        rule.Set(type, accountId, currency, amount, category, payee, note, schedule, mode, now);
        return rule;
    }

    /// <summary>
    /// Changes what the rule's next transactions say and when they are due. Transactions already posted stay
    /// as they are, and no occurrence on or before the last one posted or skipped is posted by the new schedule.
    /// The rule may keep a category that has since been archived, but not move to one. A rule set to post again
    /// posts the reminders it had, each with its due date, like any missed occurrence.
    /// </summary>
    public void Edit(
        TransactionType type, Guid accountId, Currency currency, decimal amount, Category category, string? payee, string? note,
        RecurringSchedule schedule, DateTimeOffset now, RecurringMode mode = RecurringMode.AutoPost) =>
        Set(type, accountId, currency, amount, category, payee, note, schedule, mode, now);

    /// <summary>
    /// The due dates of the rule's reminders: every occurrence due on or before today that is not settled yet,
    /// earliest first. Only a rule that reminds and is not paused has any.
    /// </summary>
    /// <param name="today">Today's date in the user's time zone.</param>
    public IEnumerable<DateOnly> Reminders(DateOnly today) =>
        Mode == RecurringMode.Remind ? Unsettled().TakeWhile(dueDate => dueDate <= today) : [];

    /// <summary>
    /// The due dates of the occurrences from today to no more than <paramref name="days"/> days after it,
    /// earliest first: one due today counts until it is posted, unless it is a reminder already. A paused rule has none.
    /// </summary>
    /// <param name="today">Today's date in the user's time zone.</param>
    public IEnumerable<DateOnly> Upcoming(DateOnly today, int days)
    {
        var last = today.AddDays(days);
        var firstUpcoming = Mode == RecurringMode.Remind ? today.AddDays(1) : today;
        return Unsettled().SkipWhile(dueDate => dueDate < firstUpcoming).TakeWhile(dueDate => dueDate <= last);
    }

    /// <summary>Whether the occurrence due on <paramref name="dueDate"/> is one of the rule's reminders today.</summary>
    public bool HasReminder(DateOnly dueDate, DateOnly today) => Reminders(today).Contains(dueDate);

    /// <summary>The user confirmed the reminder due on <paramref name="dueDate"/>, which has its transaction now: the rule moves on.</summary>
    /// <exception cref="InvalidOperationException">The rule has no such reminder.</exception>
    /// <exception cref="RecurringRuleViolation">An earlier reminder of the rule is not settled yet.</exception>
    public void Confirmed(DateOnly dueDate, DateOnly today, DateTimeOffset now) => SettleReminder(dueDate, today, now);

    /// <summary>The user dismissed the reminder due on <paramref name="dueDate"/>: it is settled with no transaction, and the rule moves on.</summary>
    /// <exception cref="InvalidOperationException">The rule has no such reminder.</exception>
    /// <exception cref="RecurringRuleViolation">An earlier reminder of the rule is not settled yet.</exception>
    public void Dismiss(DateOnly dueDate, DateOnly today, DateTimeOffset now) => SettleReminder(dueDate, today, now);

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

    /// <summary>
    /// Moves the rule from <paramref name="from"/> onto <paramref name="into"/>, as merging the one category into
    /// the other does (spec CAT-3), so it goes on posting on a category that is not archived.
    /// </summary>
    public void MoveCategory(Category from, Category into, DateTimeOffset now)
    {
        Category.GuardMerge(from, into);
        if (CategoryId != from.Id)
        {
            return;
        }

        CategoryId = into.Id;
        UpdatedAt = now;
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

    /// <summary>
    /// What is wrong with settling the reminder due on <paramref name="dueDate"/> now: an earlier one is not
    /// settled yet. Reminders are settled earliest first, so the rule only ever has to know the next one. Null
    /// when it is the next one.
    /// </summary>
    public string? ReminderOrderBroken(DateOnly dueDate) =>
        dueDate == NextRunDate ? null : string.Create(CultureInfo.InvariantCulture, $"Confirm or dismiss the reminder due on {NextRunDate:yyyy-MM-dd} first.");

    /// <summary>Settles one of the rule's reminders, the next one.</summary>
    private void SettleReminder(DateOnly dueDate, DateOnly today, DateTimeOffset now)
    {
        if (!HasReminder(dueDate, today))
        {
            throw new InvalidOperationException("Only a reminder of the rule can be settled.");
        }

        if (ReminderOrderBroken(dueDate) is { } orderRule)
        {
            throw new RecurringRuleViolation("DueDate", orderRule);
        }

        SettleThrough(dueDate, now);
    }

    /// <summary>The due dates of the occurrences not settled yet, from the next one on; none while the rule is paused.</summary>
    private IEnumerable<DateOnly> Unsettled()
    {
        if (IsPaused)
        {
            yield break;
        }

        var schedule = Schedule;
        for (var next = NextRunDate; next is { } dueDate; next = schedule.FirstAfter(dueDate))
        {
            yield return dueDate;
        }
    }

    private void SettleThrough(DateOnly date, DateTimeOffset now)
    {
        SettledThrough = date;
        NextRunDate = Schedule.FirstAfter(date);
        UpdatedAt = now;
    }

    private void Set(
        TransactionType type, Guid accountId, Currency currency, decimal amount, Category category, string? payee, string? note,
        RecurringSchedule schedule, RecurringMode mode, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a mode.");
        }

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
        Mode = mode;
        NextRunDate = schedule.FirstAfter(SettledThrough);
        UpdatedAt = now;
    }
}
