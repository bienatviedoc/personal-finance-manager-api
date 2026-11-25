using Pfm.Domain.Reports;

namespace Pfm.Domain.UnitTests.Reports;

public class TrendTests
{
    private static readonly ReportPeriod FirstQuarter = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    [Fact]
    public void A_trend_adds_up_each_month_of_the_period_and_a_month_with_nothing_is_zero()
    {
        var trend = Trend.Of(FirstQuarter,
        [
            Amount(new DateOnly(2026, 1, 3), 100m),
            Amount(new DateOnly(2026, 1, 31), 50m),
            Amount(new DateOnly(2026, 3, 1), 300m),
            Amount(new DateOnly(2026, 4, 1), 999m),
            Amount(new DateOnly(2025, 12, 31), 999m),
        ]);

        Assert.Equal([150m, 0m, 300m], trend.Months.Select(month => month.Total));
        Assert.Equal(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)],
            trend.Months.Select(month => month.Period.From));
        Assert.Equal(450m, trend.Total);
    }

    [Fact]
    public void Each_month_after_the_first_says_how_it_changed_from_the_month_before_as_an_amount_and_a_percentage()
    {
        var trend = Trend.Of(FirstQuarter,
        [
            Amount(new DateOnly(2026, 1, 3), 200m),
            Amount(new DateOnly(2026, 2, 3), 250m),
            Amount(new DateOnly(2026, 3, 3), 100m),
        ]);

        Assert.Null(trend.Months[0].Change);
        Assert.Equal(new PeriodChange(50m, 25.0m), trend.Months[1].Change);
        Assert.Equal(new PeriodChange(-150m, -60.0m), trend.Months[2].Change);
    }

    [Fact]
    public void Compared_with_the_period_before_the_first_month_changed_from_the_last_month_of_that_period()
    {
        var before = Trend.Before(FirstQuarter, [Amount(new DateOnly(2025, 12, 5), 400m)]);

        var trend = Trend.Of(FirstQuarter, [Amount(new DateOnly(2026, 1, 3), 300m)], before);

        Assert.Equal(new PeriodChange(-100m, -25.0m), trend.Months[0].Change);
        Assert.Equal(new PeriodChange(-300m, -100.0m), trend.Months[1].Change);
    }

    [Fact]
    public void A_change_from_nothing_has_no_percentage_and_one_from_below_zero_is_a_percentage_of_its_size()
    {
        Assert.Equal(new PeriodChange(300m, null), PeriodChange.Between(0m, 300m));
        Assert.Equal(new PeriodChange(300m, 150.0m), PeriodChange.Between(-200m, 100m));
        Assert.Equal(new PeriodChange(100m, 33.3m), PeriodChange.Between(300m, 400m));
        Assert.Equal(new PeriodChange(0m, 0.0m), PeriodChange.Between(300m, 300m));
    }

    [Fact]
    public void What_is_not_converted_is_left_out_of_the_totals_and_counted_in_its_month()
    {
        var waiting = Guid.CreateVersion7();
        var trend = Trend.Of(FirstQuarter,
        [
            Amount(new DateOnly(2026, 2, 3), 250m),
            new TrendAmount(waiting, new DateOnly(2026, 2, 4), null),
        ]);

        Assert.Equal(250m, trend.Total);
        Assert.Equal([waiting], trend.NotConverted);
        Assert.Equal([0, 1, 0], trend.Months.Select(month => month.NotConverted.Count));
        Assert.Equal(250m, trend.Months[1].Total);
    }

    [Fact]
    public void A_transaction_counted_twice_is_one_not_converted()
    {
        var waiting = Guid.CreateVersion7();

        var trend = Trend.Of(FirstQuarter, [new TrendAmount(waiting, new DateOnly(2026, 2, 4), null), new TrendAmount(waiting, new DateOnly(2026, 2, 4), null)]);

        Assert.Single(trend.NotConverted);
    }

    [Fact]
    public void A_trend_compared_with_another_says_how_its_total_changed()
    {
        var before = Trend.Of(FirstQuarter.Previous, [Amount(new DateOnly(2025, 11, 3), 400m)]);
        var now = Trend.Of(FirstQuarter, [Amount(new DateOnly(2026, 1, 3), 500m)]);

        Assert.Equal(new PeriodChange(100m, 25.0m), now.ChangeFrom(before));
    }

    [Fact]
    public void The_trend_before_a_period_of_whole_months_follows_as_many_calendar_months()
    {
        var before = Trend.Before(FirstQuarter, [Amount(new DateOnly(2025, 11, 3), 400m), Amount(new DateOnly(2025, 12, 31), 100m)]);

        Assert.Equal(new ReportPeriod(new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31)), before.Period);
        Assert.Equal(
            [
                new ReportPeriod(new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31)),
                new ReportPeriod(new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 30)),
                new ReportPeriod(new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)),
            ],
            before.Months.Select(month => month.Period));
        Assert.Equal([0m, 400m, 100m], before.Months.Select(month => month.Total));
    }

    [Fact]
    public void The_trend_before_any_other_period_has_a_month_for_each_of_its_months_as_many_days_earlier()
    {
        // 10 March to 20 April is 42 days: its two months are set against the 22 and 20 days just as far before them,
        // never against January, February and March cut to the previous period, which are three.
        var period = new ReportPeriod(new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 20));

        var before = Trend.Before(period, [Amount(new DateOnly(2026, 1, 27), 100m), Amount(new DateOnly(2026, 2, 17), 50m), Amount(new DateOnly(2026, 2, 18), 70m)]);

        Assert.Equal(period.Previous, before.Period);
        Assert.Equal(
            [
                new ReportPeriod(new DateOnly(2026, 1, 27), new DateOnly(2026, 2, 17)),
                new ReportPeriod(new DateOnly(2026, 2, 18), new DateOnly(2026, 3, 9)),
            ],
            before.Months.Select(month => month.Period));
        Assert.Equal([150m, 70m], before.Months.Select(month => month.Total));
        Assert.Equal(new PeriodChange(-80m, -53.3m), before.Months[1].Change);
        Assert.Equal(220m, before.Total);
    }

    private static TrendAmount Amount(DateOnly date, decimal amount) => new(Guid.CreateVersion7(), date, amount);
}
