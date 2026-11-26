namespace Pfm.Domain.Budgets;

/// <summary>
/// A spending limit for one expense category in one budget month, in the base currency. Spending in a
/// subcategory counts toward a budget on its parent, so a parent and its subcategory never both have one in
/// the same month (spec section 3, rule 6). A budget on no category is the month's overall budget: a cap on
/// all of its spending, independent of the category budgets (spec BUD-5). What was spent against it is never kept here: it is worked out
/// from transactions each time (<see cref="BudgetProgress"/>).
/// </summary>
public sealed class Budget : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private Budget()
    {
    }

    private Budget(Guid? categoryId, BudgetMonth month, Currency currency, DateTimeOffset now)
    {
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Not a supported currency.");
        }

        Id = Guid.CreateVersion7(now);
        CategoryId = categoryId;
        Month = month;
        Currency = currency;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The category the budget is on; null for the month's overall budget.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Whether this is the month's overall budget, which tracks every expense of the month.</summary>
    public bool IsOverall => CategoryId is null;

    public BudgetMonth Month { get; private set; }

    /// <summary>Never negative. Zero is a budget too: nothing is meant to be spent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// The user's base currency, which the amount was entered in. It is what locks the base currency once a
    /// budget exists (spec section 3, rule 12; decision record 0002).
    /// </summary>
    public Currency Currency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes every time the budget is saved; a change of amount names the version it was made from.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Sets a budget on a category that has none in the month yet.</summary>
    /// <param name="baseCurrency">The user's base currency.</param>
    /// <param name="budgetedThisMonth">The categories that already have a budget in <paramref name="month"/>.</param>
    public static Budget Set(
        BudgetCategory category, BudgetMonth month, decimal amount, Currency baseCurrency, IReadOnlyCollection<Guid> budgetedThisMonth, DateTimeOffset now)
    {
        EnsureCanBeSetOn(category, budgetedThisMonth);
        var budget = new Budget(category.Id, month, baseCurrency, now);
        budget.SetAmount(amount);
        return budget;
    }

    /// <summary>
    /// Sets the overall budget of a month: a cap on all of its spending, whether or not a category has a budget
    /// of its own (spec BUD-5). A month has at most one.
    /// </summary>
    /// <param name="baseCurrency">The user's base currency.</param>
    public static Budget SetOverall(BudgetMonth month, decimal amount, Currency baseCurrency, DateTimeOffset now)
    {
        var budget = new Budget(categoryId: null, month, baseCurrency, now);
        budget.SetAmount(amount);
        return budget;
    }

    /// <summary>
    /// Refuses a category that takes no new budget in a month: an income or archived one, or one whose parent
    /// or subcategory already has a budget that month (spec section 3, rule 6).
    /// </summary>
    /// <param name="budgetedThisMonth">The other categories that have a budget in the month.</param>
    public static void EnsureCanBeSetOn(BudgetCategory category, IReadOnlyCollection<Guid> budgetedThisMonth)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(budgetedThisMonth);
        if (budgetedThisMonth.Contains(category.Id))
        {
            throw new ArgumentException("The category already has a budget this month: change its amount instead.", nameof(budgetedThisMonth));
        }

        if (!category.IsExpense)
        {
            throw new BudgetRuleViolation(nameof(CategoryId), "Only an expense category takes a budget.");
        }

        if (category.IsArchived)
        {
            throw new BudgetRuleViolation(nameof(CategoryId), "An archived category takes no new budget.");
        }

        if (category.ParentId is { } parentId && budgetedThisMonth.Contains(parentId))
        {
            throw new BudgetRuleViolation(nameof(CategoryId), "Its parent category already has a budget this month, which this category's spending counts toward.");
        }

        if (category.SubcategoryIds.Any(budgetedThisMonth.Contains))
        {
            throw new BudgetRuleViolation(nameof(CategoryId), "One of its subcategories already has a budget this month. Remove that budget first.");
        }
    }

    /// <summary>The budget is now on the category its own was merged into, in the same month. &lt;see cref="BudgetMerge"/&gt; has checked it.</summary>
    internal void MoveTo(Guid categoryId, DateTimeOffset now)
    {
        CategoryId = categoryId;
        UpdatedAt = now;
    }

    public void ChangeAmount(decimal amount, DateTimeOffset now)
    {
        SetAmount(amount);
        UpdatedAt = now;
    }

    private void SetAmount(decimal amount)
    {
        if (amount < 0)
        {
            throw new BudgetRuleViolation(nameof(Amount), "A budget is not negative.");
        }

        if (!Amounts.IsValid(amount, Currency))
        {
            throw new BudgetRuleViolation(nameof(Amount), $"An amount in {Currency} has at most {Currency.DecimalPlaces()} decimals and {Amounts.MaxIntegerDigits} digits before them.");
        }

        Amount = amount;
    }
}
