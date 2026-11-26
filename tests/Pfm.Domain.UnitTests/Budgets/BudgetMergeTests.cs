using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

/// <summary>What merging one category into another does to their budgets (spec CAT-3; section 3, rule 6).</summary>
public class BudgetMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly BudgetMonth October = new(2026, 10);
    private static readonly BudgetMonth November = new(2026, 11);

    private static readonly Guid CoffeeId = Guid.CreateVersion7();
    private static readonly Guid EatingOutId = Guid.CreateVersion7();
    private static readonly Guid GroceriesId = Guid.CreateVersion7();
    private static readonly BudgetCategory Food = new(Guid.CreateVersion7(), null, [EatingOutId, GroceriesId], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Coffee = new(CoffeeId, null, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory EatingOut = new(EatingOutId, Food.Id, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Groceries = new(GroceriesId, Food.Id, [], IsExpense: true, IsArchived: false);

    [Fact]
    public void A_budget_in_a_month_the_category_merged_into_has_none_moves_to_it()
    {
        var coffee = Set(Coffee, October, 100m);

        var merge = BudgetMerge.Plan(EatingOutId, merged: [coffee], into: [], relatives: []);
        var removed = merge.Apply(Later);

        Assert.Equal(1, merge.Moving);
        Assert.Empty(removed);
        Assert.Equal((EatingOutId, October, 100m, Later), (coffee.CategoryId, coffee.Month, coffee.Amount, coffee.UpdatedAt));
    }

    [Fact]
    public void Where_both_have_a_budget_in_the_same_month_the_amounts_are_added()
    {
        var coffee = Set(Coffee, November, 100m);
        var eatingOut = Set(EatingOut, November, 250m);

        var merge = BudgetMerge.Plan(EatingOutId, [coffee], [eatingOut], []);
        var removed = merge.Apply(Later);

        Assert.Equal(1, merge.Moving);
        Assert.Equal(coffee, Assert.Single(removed));
        Assert.Equal((350m, Later), (eatingOut.Amount, eatingOut.UpdatedAt));
    }

    [Fact]
    public void A_budget_cannot_move_to_a_subcategory_whose_parent_is_budgeted_that_month()
    {
        var coffee = Set(Coffee, November, 100m);
        var food = Set(Food, November, 1000m);

        var violation = Assert.Throws<BudgetRuleViolation>(() => BudgetMerge.Plan(EatingOutId, [coffee], [], relatives: [food]));

        Assert.Equal(nameof(Budget.CategoryId), violation.Property);
        Assert.Contains("2026-11", violation.Message);
    }

    [Fact]
    public void A_budget_cannot_move_to_a_category_one_of_whose_subcategories_is_budgeted_that_month()
    {
        var coffee = Set(Coffee, November, 100m);
        var groceries = Set(Groceries, November, 1000m);

        Assert.Throws<BudgetRuleViolation>(() => BudgetMerge.Plan(Food.Id, [coffee], [], relatives: [groceries]));
    }

    [Fact]
    public void A_relative_budgeted_only_in_another_month_is_no_obstacle()
    {
        var coffee = Set(Coffee, November, 100m);
        var food = Set(Food, October, 1000m);

        Assert.Equal(1, BudgetMerge.Plan(EatingOutId, [coffee], [], [food]).Moving);
    }

    [Fact]
    public void A_category_merged_into_keeps_its_own_budget_only_where_no_new_subcategory_is_budgeted_that_month()
    {
        var food = Set(Food, November, 1000m);
        var movedSubcategory = Set(Coffee, November, 100m);

        Assert.Throws<BudgetRuleViolation>(() => BudgetMerge.Plan(Food.Id, merged: [], into: [food], relatives: [movedSubcategory]));
    }

    [Fact]
    public void Amounts_that_add_up_past_what_a_budget_holds_are_refused()
    {
        var coffee = Set(Coffee, November, 999_999_999_999_999m);
        var eatingOut = Set(EatingOut, November, 999_999_999_999_999m);

        var violation = Assert.Throws<BudgetRuleViolation>(() => BudgetMerge.Plan(EatingOutId, [coffee], [eatingOut], []));

        Assert.Equal(nameof(Budget.Amount), violation.Property);
    }

    private static Budget Set(BudgetCategory category, BudgetMonth month, decimal amount) =>
        Budget.Set(category, month, amount, Currency.VND, budgetedThisMonth: [], Now);
}
