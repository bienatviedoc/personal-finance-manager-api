using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

public class BudgetMonthTests
{
    [Fact]
    public void A_budget_month_is_the_calendar_month_from_the_1st()
    {
        var february = BudgetMonth.Of(new DateOnly(2028, 2, 17));

        Assert.Equal(new DateOnly(2028, 2, 1), february.FirstDay);
        Assert.Equal(new DateOnly(2028, 2, 29), february.LastDay);
        Assert.Equal(new BudgetMonth(2028, 2), february);
    }

    [Fact]
    public void It_is_written_as_a_year_and_a_month()
    {
        Assert.True(BudgetMonth.TryParse("2026-11", out var november));

        Assert.Equal(new BudgetMonth(2026, 11), november);
        Assert.Equal("2026-11", november.ToString());
        Assert.Equal("0987-03", new BudgetMonth(987, 3).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-1")]
    [InlineData("26-11")]
    [InlineData("2026-11-01")]
    [InlineData("November 2026")]
    [InlineData(" 2026-11")]
    public void Anything_else_is_not_a_budget_month(string? text) => Assert.False(BudgetMonth.TryParse(text, out _));

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    public void A_month_that_is_not_on_the_calendar_cannot_be_made(int year, int month) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetMonth(year, month));
}
