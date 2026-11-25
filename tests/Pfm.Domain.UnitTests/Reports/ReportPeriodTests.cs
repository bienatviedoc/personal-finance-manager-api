using Pfm.Domain.Reports;

namespace Pfm.Domain.UnitTests.Reports;

public class ReportPeriodTests
{
    [Fact]
    public void A_report_period_runs_from_one_date_to_another_both_included()
    {
        var period = new ReportPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        Assert.True(period.Contains(new DateOnly(2026, 11, 1)));
        Assert.True(period.Contains(new DateOnly(2026, 11, 30)));
        Assert.False(period.Contains(new DateOnly(2026, 10, 31)));
        Assert.False(period.Contains(new DateOnly(2026, 12, 1)));
        Assert.Equal(30, period.Days);
    }

    [Fact]
    public void The_whole_months_of_a_period_run_from_the_1st_of_its_first_month_to_the_last_day_of_its_last()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 3));

        Assert.Equal(new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28)), period.WholeMonths);
        Assert.Equal(period.WholeMonths, period.WholeMonths.WholeMonths);
        Assert.Equal(2, period.WholeMonths.Months.Count);
    }

    [Fact]
    public void A_period_cannot_end_before_it_starts() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReportPeriod(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 1)));

    [Fact]
    public void The_period_before_a_calendar_month_is_the_month_before_it()
    {
        var march = new ReportPeriod(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        Assert.Equal(new ReportPeriod(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), march.Previous);
    }

    [Fact]
    public void The_period_before_whole_months_is_as_many_whole_months_before_them()
    {
        var lastThreeMonths = new ReportPeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 30));
        var thisYear = new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(new ReportPeriod(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)), lastThreeMonths.Previous);
        Assert.Equal(new ReportPeriod(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), thisYear.Previous);
    }

    [Fact]
    public void The_period_before_any_other_is_as_many_days_just_before_it()
    {
        var tenDays = new ReportPeriod(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 14));
        var fromTheFirst = new ReportPeriod(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 20));

        Assert.Equal(new ReportPeriod(new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 4)), tenDays.Previous);
        Assert.Equal(new ReportPeriod(new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 28)), fromTheFirst.Previous);
    }

    [Fact]
    public void A_period_is_made_of_the_calendar_months_it_touches_the_first_and_last_cut_to_the_period()
    {
        var period = new ReportPeriod(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 10));

        Assert.Equal(
            [
                new ReportPeriod(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31)),
                new ReportPeriod(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
                new ReportPeriod(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10)),
            ],
            period.Months);
    }

    [Fact]
    public void A_period_within_one_month_is_one_month()
    {
        var period = new ReportPeriod(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 14));

        Assert.Equal([period], period.Months);
    }
}
