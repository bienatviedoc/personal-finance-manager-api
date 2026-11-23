namespace Pfm.Domain.Budgets;

/// <summary>
/// A spending limit for one expense category in one budget month, in the base currency. Spending in a
/// subcategory counts toward a budget on its parent, so a parent and its subcategory never both have one in
/// the same month (spec section 3, rule 6). What was spent against it is never kept here: it is worked out
/// from transactions each time (<see cref="BudgetProgress"/>).
/// </summary>
public sealed class Budget : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private Budget()
    {
    }

    private Budget(Guid categoryId, BudgetMonth month, Currency currency, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CategoryId = categoryId;
        Month = month;
        Currency = currency;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

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
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(budgetedThisMonth);
        if (!Enum.IsDefined(baseCurrency))
        {
            throw new ArgumentOutOfRangeException(nameof(baseCurrency), baseCurrency, "Not a supported currency.");
        }

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

        var budget = new Budget(category.Id, month, baseCurrency, now);
        budget.SetAmount(amount);
        return budget;
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
