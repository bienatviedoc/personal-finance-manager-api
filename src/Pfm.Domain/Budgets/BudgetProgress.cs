namespace Pfm.Domain.Budgets;

/// <summary>Whether a budget is being kept to (spec BUD-3).</summary>
public enum BudgetState
{
    /// <summary>Spent is below the user's warning threshold.</summary>
    OnTrack = 1,

    /// <summary>Spent is at or above the user's warning threshold and not above the budget.</summary>
    Warning = 2,

    /// <summary>Spent is above the budget.</summary>
    Over = 3,
}

/// <summary>
/// Whether a budget is being spent faster than its month is passing (spec BUD-6): the share of the budget spent
/// against the share of the month elapsed, today included. Only the current month has a pace.
/// </summary>
public enum BudgetPace
{
    /// <summary>The share spent is no more than the share of the month elapsed.</summary>
    OnPace = 1,

    /// <summary>A larger share of the budget is spent than of the month has passed.</summary>
    AheadOfPace = 2,
}

/// <summary>
/// What the expenses of one category in a budget month come to in the base currency, minus its refunds. Each
/// amount is as it was converted at the exchange rate of its transaction's date (decision record 0002).
/// </summary>
/// <param name="NotConverted">
/// The category's expenses that are not converted, and so are left out of <paramref name="Spent"/>, each by its
/// transaction's id: a split transaction is one expense in every category its lines are in.
/// </param>
public sealed record CategorySpending(Guid CategoryId, decimal Spent, IReadOnlyCollection<Guid> NotConverted);

/// <summary>
/// A budget against what was spent in its month: budgeted, spent, remaining and the state. Spent is derived
/// from transactions every time and can be negative; it is never clamped to zero (spec section 6).
/// </summary>
/// <param name="NotConvertedExpenses">The expenses left out of spent because no exchange rate exists on or before their date yet, by transaction id.</param>
public sealed record BudgetProgress(Budget Budget, decimal Spent, IReadOnlySet<Guid> NotConvertedExpenses, BudgetState State)
{
    /// <summary>How many expenses are left out of spent because they are not converted.</summary>
    public int NotConverted => NotConvertedExpenses.Count;

    public decimal Budgeted => Budget.Amount;

    /// <summary>What is left of the budget; negative once over.</summary>
    public decimal Remaining => Budgeted - Spent;

    /// <summary>
    /// Spent as whole percent of the budget, rounded down so that it never shows a state the budget is not in
    /// yet (79.9% is 79, still on track). Null for a budget of zero, which nothing is a percentage of.
    /// </summary>
    public int? PercentSpent => PercentOf(Budgeted, Spent);

    /// <summary>Spent as whole percent of what was budgeted, rounded down; null when nothing was budgeted.</summary>
    internal static int? PercentOf(decimal budgeted, decimal spent) =>
        budgeted == 0 ? null : (int)Math.Clamp(Math.Floor(spent * 100 / budgeted), int.MinValue, int.MaxValue);

    /// <summary>A warning threshold is a whole percentage from 1 to 100.</summary>
    public static bool IsValidWarningPercent(int percent) => percent is >= 1 and <= 100;

    /// <summary>
    /// The progress of <paramref name="budget"/>: what was spent in its category and, when that is a top-level
    /// one, in its subcategories (spec section 3, rule 6).
    /// </summary>
    /// <param name="category">The category the budget is on.</param>
    /// <param name="spending">What was spent in the budget's month, by category; categories with nothing spent may be left out.</param>
    /// <param name="warningPercent">The user's warning threshold.</param>
    public static BudgetProgress Of(Budget budget, BudgetCategory category, IEnumerable<CategorySpending> spending, int warningPercent)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(spending);
        if (category.Id != budget.CategoryId)
        {
            throw new ArgumentException("Progress is worked out for the budget's own category.", nameof(category));
        }

        return Counting(budget, spending.Where(of => of.CategoryId == category.Id || category.SubcategoryIds.Contains(of.CategoryId)), warningPercent);
    }

    /// <summary>
    /// The progress of the month's overall budget: what was spent in every category, whether or not it has a
    /// budget of its own (spec BUD-5).
    /// </summary>
    /// <param name="spending">What was spent in the budget's month, by category.</param>
    /// <param name="warningPercent">The user's warning threshold.</param>
    public static BudgetProgress OfOverall(Budget budget, IEnumerable<CategorySpending> spending, int warningPercent)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(spending);
        if (!budget.IsOverall)
        {
            throw new ArgumentException("Only the overall budget counts every category's spending.", nameof(budget));
        }

        return Counting(budget, spending, warningPercent);
    }

    /// <summary>
    /// Whether the budget is being spent faster than its month is passing, on <paramref name="today"/> in the
    /// user's time zone (spec BUD-6); null unless today is in the budget's month, so a past month shows only
    /// its final result.
    /// </summary>
    public BudgetPace? PaceOn(DateOnly today)
    {
        var month = Budget.Month;
        if (month.PercentElapsedOn(today) is null)
        {
            return null;
        }

        // Spent / budgeted above day / days, multiplied out so that a budget of zero needs no division.
        return Spent * month.Days > Budgeted * today.Day ? BudgetPace.AheadOfPace : BudgetPace.OnPace;
    }

    private static BudgetProgress Counting(Budget budget, IEnumerable<CategorySpending> spending, int warningPercent)
    {
        if (!IsValidWarningPercent(warningPercent))
        {
            throw new ArgumentOutOfRangeException(nameof(warningPercent), warningPercent, "A warning threshold is from 1 to 100 percent.");
        }

        var counted = spending.ToList();
        var spent = counted.Sum(of => of.Spent);
        return new BudgetProgress(budget, spent, counted.SelectMany(of => of.NotConverted).ToHashSet(), StateOf(budget.Amount, spent, warningPercent));
    }

    /// <summary>
    /// Over above 100% of the budget; a warning at or above the threshold; otherwise on track. A budget of
    /// zero with nothing spent is on track: there is nothing to warn about yet.
    /// </summary>
    internal static BudgetState StateOf(decimal budgeted, decimal spent, int warningPercent) =>
        spent > budgeted ? BudgetState.Over
        : budgeted > 0 && spent * 100 >= budgeted * warningPercent ? BudgetState.Warning
        : BudgetState.OnTrack;
}

/// <summary>The budgets of one budget month added up (spec BUD-2).</summary>
/// <param name="NotConverted">How many expenses are left out of <paramref name="Spent"/> because they are not converted, each counted once.</param>
public sealed record BudgetTotals(decimal Budgeted, decimal Spent, decimal Remaining, int NotConverted)
{
    public static BudgetTotals Of(IReadOnlyCollection<BudgetProgress> budgets) =>
        new(budgets.Sum(budget => budget.Budgeted), budgets.Sum(budget => budget.Spent), budgets.Sum(budget => budget.Remaining),
            budgets.SelectMany(budget => budget.NotConvertedExpenses).Distinct().Count());
}
