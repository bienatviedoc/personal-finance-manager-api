namespace Pfm.Domain.Budgets;

/// <summary>
/// Tells a user that a category's spending moved into warning or over its budget in a budget month (spec BUD-7).
/// It is shown in the app and emailed. Each category raises at most one warning and one over a month, even when
/// spent drops back and crosses again (spec section 6), so a notification is kept once raised, and what the
/// budget stood at then is kept with it: spent itself is never stored, and moves on.
/// </summary>
public sealed class BudgetNotification : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private BudgetNotification()
    {
    }

    private BudgetNotification(Guid categoryId, BudgetMonth month, BudgetState state, decimal budgeted, decimal spent, Currency currency, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CategoryId = categoryId;
        Month = month;
        State = state;
        Budgeted = budgeted;
        Spent = spent;
        Currency = currency;
        RaisedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The category whose budget it is about.</summary>
    public Guid CategoryId { get; private set; }

    public BudgetMonth Month { get; private set; }

    /// <summary>The state the budget moved into: <see cref="BudgetState.Warning"/> or <see cref="BudgetState.Over"/>.</summary>
    public BudgetState State { get; private set; }

    /// <summary>The budget's amount when it was raised.</summary>
    public decimal Budgeted { get; private set; }

    /// <summary>What was spent when it was raised.</summary>
    public decimal Spent { get; private set; }

    /// <summary>The base currency the amounts are in.</summary>
    public Currency Currency { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    /// <summary>Spent as whole percent of the budget when it was raised, rounded down as on the budget; null for a budget of zero.</summary>
    public int? PercentSpent => Budgeted == 0 ? null : (int)Math.Clamp(Math.Floor(Spent * 100 / Budgeted), int.MinValue, int.MaxValue);

    /// <summary>When the user read it in the app; null while unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>
    /// The notification a budget in <paramref name="state"/> raises, given those its category has raised in the
    /// month already; null when it raises none. Over is raised once; a warning once, and not after over: a budget
    /// that drops back from over has not moved into warning.
    /// </summary>
    public static BudgetState? Due(BudgetState state, IReadOnlyCollection<BudgetState> raised)
    {
        ArgumentNullException.ThrowIfNull(raised);
        return state switch
        {
            BudgetState.Over when !raised.Contains(BudgetState.Over) => BudgetState.Over,
            BudgetState.Warning when raised.Count == 0 => BudgetState.Warning,
            _ => null,
        };
    }

    public static BudgetNotification Raise(
        Guid categoryId, BudgetMonth month, BudgetState state, decimal budgeted, decimal spent, Currency currency, DateTimeOffset now)
    {
        if (state is not (BudgetState.Warning or BudgetState.Over))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Only a budget in warning or over raises a notification.");
        }

        return new BudgetNotification(categoryId, month, state, budgeted, spent, currency, now);
    }

    /// <summary>Marks it read; reading it again changes nothing.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
