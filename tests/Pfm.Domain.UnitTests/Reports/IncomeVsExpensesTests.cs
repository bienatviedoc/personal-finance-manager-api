using Pfm.Domain.Reports;

namespace Pfm.Domain.UnitTests.Reports;

public class IncomeVsExpensesTests
{
    private static readonly Guid Waiting = Guid.NewGuid();

    [Fact]
    public void Every_month_the_period_touches_has_its_income_expenses_net_and_savings_rate_empty_months_included()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        var report = IncomeVsExpenses.Of(period,
        [
            Flow(new DateOnly(2026, 1, 5), income: 20_000_000m),
            Flow(new DateOnly(2026, 1, 20), expenses: 15_000_000m),
            Flow(new DateOnly(2026, 3, 31), income: 10_000_000m),
            Flow(new DateOnly(2026, 3, 1), expenses: 12_500_000m),
        ]);

        Assert.Equal(
            [
                (new DateOnly(2026, 1, 1), 20_000_000m, 15_000_000m, 5_000_000m, (decimal?)25.0m),
                (new DateOnly(2026, 2, 1), 0m, 0m, 0m, null),
                (new DateOnly(2026, 3, 1), 10_000_000m, 12_500_000m, -2_500_000m, -25.0m),
            ],
            report.Months.Select(month => (month.Month, month.Totals.Income, month.Totals.Expenses, month.Totals.Net, month.Totals.SavingsRate)));
        Assert.Equal((30_000_000m, 27_500_000m, 2_500_000m, (decimal?)8.3m), (report.Total.Income, report.Total.Expenses, report.Total.Net, report.Total.SavingsRate));
    }

    [Fact]
    public void A_period_that_starts_or_ends_inside_a_month_shows_that_month_from_or_to_its_own_day()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 10));

        var report = IncomeVsExpenses.Of(period, []);

        Assert.Equal(
            [(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31)), (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 10))],
            report.Months.Select(month => (month.Month, month.Period.From, month.Period.To)));
    }

    [Fact]
    public void Without_income_above_zero_there_is_no_savings_rate()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var report = IncomeVsExpenses.Of(period, [Flow(new DateOnly(2026, 1, 3), expenses: 100m)]);

        Assert.Null(report.Total.SavingsRate);
        Assert.Equal(-100m, report.Total.Net);
    }

    [Fact]
    public void What_is_not_converted_is_counted_once_in_its_month_and_in_the_total()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));

        var report = IncomeVsExpenses.Of(period,
        [
            new TransactionFlow(Waiting, new DateOnly(2026, 2, 3), 0m, 0m, IsConverted: false),
            Flow(new DateOnly(2026, 2, 3), income: 100m),
        ]);

        Assert.Equal([0, 1], report.Months.Select(month => month.Totals.NotConverted.Count));
        Assert.Equal([Waiting], report.Total.NotConverted);
        Assert.Equal(100m, report.Total.Income);
    }

    [Fact]
    public void What_is_dated_outside_the_period_is_left_out()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var report = IncomeVsExpenses.Of(period, [Flow(new DateOnly(2026, 2, 1), income: 100m)]);

        Assert.Equal(0m, report.Total.Income);
    }

    [Fact]
    public void The_period_before_one_that_is_not_whole_months_has_one_month_for_each_of_the_periods_at_the_same_place()
    {
        // 10 March to 20 April is 42 days: the period before is 27 January to 9 March.
        var period = new ReportPeriod(new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 20));

        var before = IncomeVsExpenses.Before(period,
        [
            Flow(new DateOnly(2026, 1, 27), income: 100m),
            Flow(new DateOnly(2026, 2, 17), expenses: 40m),
            Flow(new DateOnly(2026, 2, 18), income: 30m),
            Flow(new DateOnly(2026, 3, 9), expenses: 5m),
            Flow(new DateOnly(2026, 3, 10), income: 1_000m),
        ]);

        Assert.Equal(
            [
                (new DateOnly(2026, 1, 27), new DateOnly(2026, 2, 17), 100m, 40m),
                (new DateOnly(2026, 2, 18), new DateOnly(2026, 3, 9), 30m, 5m),
            ],
            before.Months.Select(month => (month.Period.From, month.Period.To, month.Totals.Income, month.Totals.Expenses)));
        Assert.Equal(130m, before.Total.Income);
    }

    private static TransactionFlow Flow(DateOnly date, decimal income = 0, decimal expenses = 0) => new(Guid.NewGuid(), date, income, expenses, IsConverted: true);
}
