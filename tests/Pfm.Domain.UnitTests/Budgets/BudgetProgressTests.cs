using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

/// <summary>Budgeted, spent, remaining and the state of a budget (spec section 6: BUD-2, BUD-3 and the calculation).</summary>
public class BudgetProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth November = new(2026, 11);

    private static readonly Guid GroceriesId = Guid.CreateVersion7();
    private static readonly Guid RestaurantsId = Guid.CreateVersion7();
    private static readonly BudgetCategory Food = new(Guid.CreateVersion7(), ParentId: null, [GroceriesId, RestaurantsId], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Groceries = new(GroceriesId, Food.Id, [], IsExpense: true, IsArchived: false);
    private static readonly BudgetCategory Transport = new(Guid.CreateVersion7(), ParentId: null, [], IsExpense: true, IsArchived: false);

    private static Budget BudgetOf(decimal amount, BudgetCategory? category = null) => Budget.Set(category ?? Food, November, amount, Currency.VND, [], Now);

    private static BudgetProgress Progress(decimal budgeted, decimal spent, int warningPercent = 80) =>
        BudgetProgress.Of(BudgetOf(budgeted), Food, [new CategorySpending(Food.Id, spent, NotConverted: 0)], warningPercent);

    // Roll-up (spec section 3, rule 6).

    [Fact]
    public void Spending_in_a_subcategory_counts_toward_a_budget_on_its_parent()
    {
        CategorySpending[] spending =
        [
            new(Food.Id, 200_000m, 0), new(GroceriesId, 1_250_000m, 0), new(RestaurantsId, 400_000m, 0), new(Transport.Id, 90_000m, 0),
        ];

        var food = BudgetProgress.Of(BudgetOf(3_000_000m), Food, spending, warningPercent: 80);

        Assert.Equal((3_000_000m, 1_850_000m, 1_150_000m), (food.Budgeted, food.Spent, food.Remaining));
    }

    [Fact]
    public void A_budget_on_a_subcategory_counts_only_what_was_spent_in_it()
    {
        CategorySpending[] spending = [new(Food.Id, 200_000m, 0), new(GroceriesId, 1_250_000m, 0), new(RestaurantsId, 400_000m, 0)];

        var groceries = BudgetProgress.Of(BudgetOf(2_000_000m, Groceries), Groceries, spending, warningPercent: 80);

        Assert.Equal((1_250_000m, 750_000m), (groceries.Spent, groceries.Remaining));
    }

    [Fact]
    public void With_nothing_spent_the_whole_budget_remains()
    {
        var food = BudgetProgress.Of(BudgetOf(3_000_000m), Food, [new CategorySpending(Transport.Id, 90_000m, 0)], warningPercent: 80);

        Assert.Equal((0m, 3_000_000m, BudgetState.OnTrack, 0), (food.Spent, food.Remaining, food.State, food.PercentSpent));
    }

    [Fact]
    public void Progress_is_of_a_budget_and_its_own_category()
    {
        Assert.Throws<ArgumentException>(() => BudgetProgress.Of(BudgetOf(1m), Transport, [], warningPercent: 80));
    }

    // Conversion (spec section 3, rule 7; decision record 0002).

    [Fact]
    public void Spent_adds_up_amounts_as_they_were_converted_into_the_base_currency_and_leaves_out_and_counts_those_not_converted()
    {
        // Groceries holds 40 USD converted at its date's rate of 25,000 with 250,000 VND; Restaurants one expense in VND and two waiting for a rate.
        CategorySpending[] spending = [new(GroceriesId, 1_000_000m + 250_000m, NotConverted: 0), new(RestaurantsId, 400_000m, NotConverted: 2), new(Food.Id, 0m, NotConverted: 1)];

        var food = BudgetProgress.Of(BudgetOf(3_000_000m), Food, spending, warningPercent: 80);

        Assert.Equal((1_650_000m, 3), (food.Spent, food.NotConverted));
    }

    // The three states (BUD-3).

    [Theory]
    [InlineData("1000", "0", BudgetState.OnTrack)]
    [InlineData("1000", "799.99", BudgetState.OnTrack)]
    [InlineData("1000", "800", BudgetState.Warning)]
    [InlineData("1000", "999.99", BudgetState.Warning)]
    [InlineData("1000", "1000", BudgetState.Warning)]
    [InlineData("1000", "1000.01", BudgetState.Over)]
    [InlineData("1000", "25000", BudgetState.Over)]
    public void A_budget_is_on_track_then_at_or_above_80_percent_a_warning_then_above_100_percent_over(string budgeted, string spent, BudgetState state) =>
        Assert.Equal(state, Progress(Amount(budgeted), Amount(spent)).State);

    [Theory]
    [InlineData(50, "499.99", BudgetState.OnTrack)]
    [InlineData(50, "500", BudgetState.Warning)]
    [InlineData(95, "900", BudgetState.OnTrack)]
    [InlineData(95, "950", BudgetState.Warning)]
    [InlineData(100, "999.99", BudgetState.OnTrack)]
    [InlineData(100, "1000", BudgetState.Warning)]
    [InlineData(100, "1000.01", BudgetState.Over)]
    [InlineData(1, "10", BudgetState.Warning)]
    public void The_warning_starts_at_the_users_threshold(int warningPercent, string spent, BudgetState state) =>
        Assert.Equal(state, Progress(1000m, Amount(spent), warningPercent).State);

    [Fact]
    public void A_budget_of_zero_is_on_track_until_anything_is_spent_and_then_over()
    {
        Assert.Equal((BudgetState.OnTrack, (int?)null), (Progress(0m, 0m).State, Progress(0m, 0m).PercentSpent));
        Assert.Equal(BudgetState.Over, Progress(0m, 0.01m).State);
    }

    [Fact]
    public void Spent_can_be_negative_and_is_never_clamped_to_zero()
    {
        var refunded = Progress(1000m, -250m);

        Assert.Equal((-250m, 1250m, BudgetState.OnTrack), (refunded.Spent, refunded.Remaining, refunded.State));
    }

    [Fact]
    public void Remaining_is_negative_once_over()
    {
        Assert.Equal(-500m, Progress(1000m, 1500m).Remaining);
    }

    [Theory]
    [InlineData("799.99", 79)]
    [InlineData("800", 80)]
    [InlineData("1000", 100)]
    [InlineData("1000.01", 100)]
    [InlineData("1519", 151)]
    [InlineData("-250", -25)]
    public void The_percentage_spent_never_rounds_up_into_the_next_state(string spent, int percent) =>
        Assert.Equal(percent, Progress(1000m, Amount(spent)).PercentSpent);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void A_warning_threshold_is_a_percentage_from_1_to_100(int warningPercent)
    {
        Assert.False(BudgetProgress.IsValidWarningPercent(warningPercent));
        Assert.Throws<ArgumentOutOfRangeException>(() => Progress(1000m, 0m, warningPercent));
    }

    // Totals (BUD-2).

    [Fact]
    public void Totals_add_up_budgeted_spent_and_remaining_over_every_budget_of_the_month()
    {
        CategorySpending[] spending = [new(GroceriesId, 1_250_000m, 1), new(Transport.Id, 650_000m, 0)];
        BudgetProgress[] month =
        [
            BudgetProgress.Of(BudgetOf(3_000_000m), Food, spending, 80),
            BudgetProgress.Of(BudgetOf(500_000m, Transport), Transport, spending, 80),
        ];

        var totals = BudgetTotals.Of(month);

        Assert.Equal(new BudgetTotals(3_500_000m, 1_900_000m, 1_600_000m, NotConverted: 1), totals);
    }

    [Fact]
    public void A_month_with_no_budgets_totals_zero() => Assert.Equal(new BudgetTotals(0m, 0m, 0m, 0), BudgetTotals.Of([]));

    private static decimal Amount(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
