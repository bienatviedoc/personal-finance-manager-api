using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

public class BudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly BudgetMonth November = new(2026, 11);

    private static readonly Guid GroceriesId = Guid.CreateVersion7();
    private static readonly Guid RestaurantsId = Guid.CreateVersion7();
    private static readonly BudgetCategory Food = new(Guid.CreateVersion7(), ParentId: null, [GroceriesId, RestaurantsId], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Groceries = new(GroceriesId, Food.Id, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Restaurants = new(RestaurantsId, Food.Id, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Salary = new(Guid.CreateVersion7(), ParentId: null, [], IsExpense: false, IsArchived: false);

    [Fact]
    public void A_budget_is_a_spending_limit_for_one_expense_category_in_one_budget_month_in_the_base_currency()
    {
        var budget = Budget.Set(Food, November, 5_000_000m, Currency.VND, budgetedThisMonth: [], Now);

        Assert.Equal((Food.Id, November, 5_000_000m, Currency.VND), (budget.CategoryId, budget.Month, budget.Amount, budget.Currency));
        Assert.Equal((Now, Now), (budget.CreatedAt, budget.UpdatedAt));
        Assert.NotEqual(Guid.Empty, budget.Id);
    }

    [Fact]
    public void An_income_category_takes_no_budget()
    {
        var violation = Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Salary, November, 100m, Currency.VND, [], Now));

        Assert.Equal(nameof(Budget.CategoryId), violation.Property);
    }

    [Fact]
    public void An_archived_category_takes_no_new_budget()
    {
        var violation = Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Food with { IsArchived = true }, November, 100m, Currency.VND, [], Now));

        Assert.Equal(nameof(Budget.CategoryId), violation.Property);
    }

    [Fact]
    public void A_budget_cannot_be_set_on_a_subcategory_whose_parent_has_one_that_month()
    {
        var violation = Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Groceries, November, 100m, Currency.VND, budgetedThisMonth: [Food.Id], Now));

        Assert.Equal(nameof(Budget.CategoryId), violation.Property);
    }

    [Fact]
    public void A_budget_cannot_be_set_on_a_category_one_of_whose_subcategories_has_one_that_month()
    {
        var violation = Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Food, November, 100m, Currency.VND, budgetedThisMonth: [Restaurants.Id], Now));

        Assert.Equal(nameof(Budget.CategoryId), violation.Property);
    }

    [Fact]
    public void Two_subcategories_of_one_parent_can_each_have_a_budget_in_the_same_month()
    {
        var budget = Budget.Set(Groceries, November, 100m, Currency.VND, budgetedThisMonth: [Restaurants.Id], Now);

        Assert.Equal(Groceries.Id, budget.CategoryId);
    }

    [Fact]
    public void A_category_has_one_budget_a_month()
    {
        Assert.Throws<ArgumentException>(() => Budget.Set(Food, November, 100m, Currency.VND, budgetedThisMonth: [Food.Id], Now));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0.01")]
    public void A_budget_is_not_negative(string amount)
    {
        var violation = Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Food, November, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency.USD, [], Now));

        Assert.Equal(nameof(Budget.Amount), violation.Property);
    }

    [Fact]
    public void A_budget_of_zero_is_a_budget() => Assert.Equal(0m, Budget.Set(Food, November, 0m, Currency.VND, [], Now).Amount);

    [Fact]
    public void A_budget_has_no_more_decimals_than_the_base_currency()
    {
        Assert.Equal(nameof(Budget.Amount), Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Food, November, 100.5m, Currency.VND, [], Now)).Property);
        Assert.Equal(nameof(Budget.Amount), Assert.Throws<BudgetRuleViolation>(() => Budget.Set(Food, November, 100.555m, Currency.USD, [], Now)).Property);
        Assert.Equal(100.55m, Budget.Set(Food, November, 100.55m, Currency.USD, [], Now).Amount);
    }

    [Fact]
    public void Its_amount_can_be_changed_under_the_same_rules()
    {
        var budget = Budget.Set(Food, November, 5_000_000m, Currency.VND, [], Now);

        budget.ChangeAmount(6_000_000m, Later);

        Assert.Equal((6_000_000m, Now, Later), (budget.Amount, budget.CreatedAt, budget.UpdatedAt));
        Assert.Throws<BudgetRuleViolation>(() => budget.ChangeAmount(-1m, Later));
        Assert.Throws<BudgetRuleViolation>(() => budget.ChangeAmount(0.5m, Later));
        Assert.Equal(6_000_000m, budget.Amount);
    }
}
