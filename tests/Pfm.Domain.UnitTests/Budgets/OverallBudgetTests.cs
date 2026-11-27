using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

/// <summary>The overall monthly budget (spec BUD-5) and the pace indicator (spec BUD-6).</summary>
public class OverallBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth November = new(2026, 11);

    private static readonly Guid GroceriesId = Guid.CreateVersion7();
    private static readonly BudgetCategory Food = new(Guid.CreateVersion7(), ParentId: null, [GroceriesId], IsExpense: true, IsArchived: false);
    private static readonly Guid TransportId = Guid.CreateVersion7();
    private static readonly Guid UnbudgetedId = Guid.CreateVersion7();

    private static Budget Overall(decimal amount) => Budget.SetOverall(November, amount, Currency.VND, Now);

    private static BudgetProgress OverallProgress(decimal budgeted, decimal spent) =>
        BudgetProgress.OfOverall(Overall(budgeted), [new CategorySpending(TransportId, spent, [])], warningPercent: 80);

    private static BudgetProgress FoodProgress(decimal budgeted, decimal spent) =>
        BudgetProgress.Of(Budget.Set(Food, November, budgeted, Currency.VND, [], Now), Food, [new CategorySpending(Food.Id, spent, [])], warningPercent: 80);

    // The overall budget (BUD-5).

    [Fact]
    public void An_overall_budget_caps_a_months_total_spending_and_is_on_no_category()
    {
        var overall = Overall(10_000_000m);

        Assert.True(overall.IsOverall);
        Assert.Null(overall.CategoryId);
        Assert.Equal((November, 10_000_000m, Currency.VND), (overall.Month, overall.Amount, overall.Currency));
        Assert.False(Budget.Set(Food, November, 1m, Currency.VND, [], Now).IsOverall);
    }

    [Fact]
    public void An_overall_budget_follows_the_rules_of_an_amount()
    {
        Assert.Equal(nameof(Budget.Amount), Assert.Throws<BudgetRuleViolation>(() => Overall(-1m)).Property);
        Assert.Equal(nameof(Budget.Amount), Assert.Throws<BudgetRuleViolation>(() => Overall(0.5m)).Property);
        Assert.Equal(0m, Overall(0m).Amount);
    }

    [Fact]
    public void The_overall_budget_tracks_every_expense_whether_or_not_its_category_has_a_budget_of_its_own()
    {
        var (waiting, split) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        CategorySpending[] spending =
        [
            new(Food.Id, 200_000m, [split]), new(GroceriesId, 1_250_000m, []), new(TransportId, 400_000m, [waiting, split]), new(UnbudgetedId, -50_000m, []),
        ];

        var overall = BudgetProgress.OfOverall(Overall(5_000_000m), spending, warningPercent: 80);

        Assert.Equal((5_000_000m, 1_800_000m, 3_200_000m, BudgetState.OnTrack, 36, 2),
            (overall.Budgeted, overall.Spent, overall.Remaining, overall.State, overall.PercentSpent, overall.NotConverted));
    }

    [Fact]
    public void The_overall_budget_has_the_same_states_as_a_category_budget()
    {
        Assert.Equal(BudgetState.Warning, OverallProgress(1000m, 800m).State);
        Assert.Equal(BudgetState.Over, OverallProgress(1000m, 1000.01m).State);
    }

    [Fact]
    public void Progress_of_one_kind_of_budget_is_not_worked_out_as_the_other()
    {
        Assert.Throws<ArgumentException>(() => BudgetProgress.OfOverall(Budget.Set(Food, November, 1m, Currency.VND, [], Now), [], 80));
        Assert.Throws<ArgumentException>(() => BudgetProgress.Of(Overall(1m), Food, [], 80));
    }

    // The pace indicator (BUD-6): November has 30 days.

    [Theory]
    [InlineData(15, "1500000", BudgetPace.OnPace)]
    [InlineData(15, "1500001", BudgetPace.AheadOfPace)]
    [InlineData(1, "100000", BudgetPace.OnPace)]
    [InlineData(1, "100001", BudgetPace.AheadOfPace)]
    [InlineData(30, "3000000", BudgetPace.OnPace)]
    [InlineData(30, "3000001", BudgetPace.AheadOfPace)]
    [InlineData(10, "-250000", BudgetPace.OnPace)]
    public void The_pace_compares_the_share_of_the_budget_spent_with_the_share_of_the_month_elapsed_today_included(int day, string spent, BudgetPace pace)
    {
        var today = new DateOnly(2026, 11, day);
        var amount = decimal.Parse(spent, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(pace, FoodProgress(3_000_000m, amount).PaceOn(today));
        Assert.Equal(pace, OverallProgress(3_000_000m, amount).PaceOn(today));
    }

    [Fact]
    public void A_budget_of_zero_is_on_pace_until_anything_is_spent()
    {
        var today = new DateOnly(2026, 11, 15);

        Assert.Equal(BudgetPace.OnPace, OverallProgress(0m, 0m).PaceOn(today));
        Assert.Equal(BudgetPace.AheadOfPace, OverallProgress(0m, 0.01m).PaceOn(today));
    }

    [Fact]
    public void A_past_month_shows_its_final_result_with_no_pace_and_a_month_still_to_come_has_none_either()
    {
        Assert.Null(FoodProgress(3_000_000m, 3_500_000m).PaceOn(new DateOnly(2026, 12, 1)));
        Assert.Null(OverallProgress(3_000_000m, 0m).PaceOn(new DateOnly(2026, 10, 31)));
    }

    [Theory]
    [InlineData(2026, 11, 1, 3)]
    [InlineData(2026, 11, 15, 50)]
    [InlineData(2026, 11, 30, 100)]
    public void The_share_of_the_month_elapsed_counts_today_and_is_rounded_down(int year, int month, int day, int percent) =>
        Assert.Equal(percent, November.PercentElapsedOn(new DateOnly(year, month, day)));

    [Fact]
    public void Only_the_current_month_has_a_share_elapsed()
    {
        Assert.Null(November.PercentElapsedOn(new DateOnly(2026, 12, 1)));
        Assert.Null(November.PercentElapsedOn(new DateOnly(2026, 10, 31)));
    }
}
