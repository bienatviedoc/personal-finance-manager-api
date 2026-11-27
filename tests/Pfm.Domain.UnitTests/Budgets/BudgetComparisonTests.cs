using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

/// <summary>Budgets against what was spent over one or several budget months (spec RPT-6), with the spending no budget covers.</summary>
public class BudgetComparisonTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth February = new(2026, 2);
    private static readonly BudgetMonth March = new(2026, 3);

    private static readonly Guid GroceriesId = Guid.CreateVersion7();
    private static readonly Guid RestaurantsId = Guid.CreateVersion7();
    private static readonly BudgetCategory Food = new(Guid.CreateVersion7(), ParentId: null, [GroceriesId, RestaurantsId], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Groceries = new(GroceriesId, Food.Id, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Restaurants = new(RestaurantsId, Food.Id, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Transport = new(Guid.CreateVersion7(), ParentId: null, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory[] Categories = [Food, Groceries, Restaurants, Transport];

    private static Budget BudgetOf(BudgetCategory category, BudgetMonth month, decimal amount) => Budget.Set(category, month, amount, Currency.VND, [], Now);

    private static CategorySpending Spent(Guid categoryId, decimal spent, IReadOnlyCollection<Guid> notConverted) => new(categoryId, spent, notConverted);

    private static MonthSpending In(BudgetMonth month, params CategorySpending[] spending) => new(month, spending);

    [Fact]
    public void A_budget_is_compared_with_what_was_spent_in_its_category_and_subcategories_that_month()
    {
        var comparison = BudgetComparison.Of(
            [BudgetOf(Food, March, 3_000_000m)],
            Categories,
            [In(March, Spent(Food.Id, 200_000m, []), Spent(GroceriesId, 1_250_000m, [])), In(February, Spent(GroceriesId, 999_000m, []))],
            warningPercent: 80);

        var food = Assert.Single(comparison.Budgets);
        Assert.Equal((Food.Id, 1, 3_000_000m, 1_450_000m, 1_550_000m, (int?)48, BudgetState.OnTrack), (food.CategoryId, food.Months, food.Budgeted, food.Spent, food.Remaining, food.PercentSpent, food.State));
    }

    [Fact]
    public void The_overall_budget_is_not_compared_as_a_category_and_covers_no_categorys_spending()
    {
        var comparison = BudgetComparison.Of(
            [Budget.SetOverall(March, 10_000_000m, Currency.VND, Now), BudgetOf(Food, March, 3_000_000m)],
            Categories,
            [In(March, Spent(GroceriesId, 1_000_000m, []), Spent(Transport.Id, 70_000m, []))],
            warningPercent: 80);

        Assert.Equal(Food.Id, Assert.Single(comparison.Budgets).CategoryId);
        Assert.Equal((Transport.Id, 70_000m), (Assert.Single(comparison.Unbudgeted).CategoryId, comparison.Unbudgeted[0].Spent));
    }

    [Fact]
    public void Across_several_months_a_categorys_budgets_and_what_was_spent_against_them_are_summed()
    {
        var comparison = BudgetComparison.Of(
            [BudgetOf(Transport, February, 500_000m), BudgetOf(Transport, March, 500_000m)],
            Categories,
            [In(February, Spent(Transport.Id, 600_000m, [])), In(March, Spent(Transport.Id, 350_000m, []))],
            warningPercent: 80);

        var transport = Assert.Single(comparison.Budgets);
        Assert.Equal((2, 1_000_000m, 950_000m, 50_000m, (int?)95, BudgetState.Warning), (transport.Months, transport.Budgeted, transport.Spent, transport.Remaining, transport.PercentSpent, transport.State));
    }

    [Fact]
    public void Spent_above_the_summed_budgets_is_over()
    {
        var comparison = BudgetComparison.Of(
            [BudgetOf(Transport, March, 100_000m)], Categories, [In(March, Spent(Transport.Id, 100_001m, []))], warningPercent: 80);

        Assert.Equal((BudgetState.Over, -1m), (comparison.Budgets[0].State, comparison.Budgets[0].Remaining));
    }

    [Fact]
    public void Spending_no_budget_covers_that_month_is_listed_apart_by_the_category_it_was_spent_in()
    {
        var comparison = BudgetComparison.Of(
            [BudgetOf(Groceries, March, 2_000_000m), BudgetOf(Transport, February, 400_000m)],
            Categories,
            [
                In(February, Spent(Transport.Id, 300_000m, []), Spent(RestaurantsId, 100_000m, [])),
                In(March, Spent(GroceriesId, 1_000_000m, []), Spent(RestaurantsId, 250_000m, []), Spent(Food.Id, 50_000m, []), Spent(Transport.Id, 70_000m, [])),
            ],
            warningPercent: 80);

        Assert.Equal(
            [(GroceriesId, 1_000_000m), (Transport.Id, 300_000m)],
            comparison.Budgets.Select(budget => (budget.CategoryId, budget.Spent)).OrderBy(row => row.Item2).Reverse());
        Assert.Equal(
            [(RestaurantsId, 350_000m), (Transport.Id, 70_000m), (Food.Id, 50_000m)],
            comparison.Unbudgeted.Select(spending => (spending.CategoryId, spending.Spent)).OrderByDescending(row => row.Item2));
    }

    [Fact]
    public void Expenses_that_are_not_converted_are_counted_once_on_the_budget_or_the_spending_they_are_left_out_of()
    {
        var split = Guid.CreateVersion7();
        var abroad = Guid.CreateVersion7();

        var comparison = BudgetComparison.Of(
            [BudgetOf(Food, March, 1_000_000m)],
            Categories,
            [In(March, Spent(GroceriesId, 0m, [split, abroad]), Spent(RestaurantsId, 0m, [split]), Spent(Transport.Id, 0m, [split]))],
            warningPercent: 80);

        Assert.True(comparison.Budgets[0].NotConvertedExpenses.SetEquals([split, abroad]));
        Assert.Equal([split], comparison.Unbudgeted.Single().NotConvertedExpenses);
    }

    [Fact]
    public void A_budget_of_zero_has_no_percentage()
    {
        var comparison = BudgetComparison.Of([BudgetOf(Transport, March, 0m)], Categories, [], warningPercent: 80);

        Assert.Equal(((int?)null, BudgetState.OnTrack), (comparison.Budgets[0].PercentSpent, comparison.Budgets[0].State));
    }
}
