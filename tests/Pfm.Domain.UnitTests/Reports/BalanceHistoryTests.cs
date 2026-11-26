using Pfm.Domain.Reports;

namespace Pfm.Domain.UnitTests.Reports;

public class BalanceHistoryTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void A_daily_running_balance_has_a_balance_at_the_end_of_every_day_of_the_period()
    {
        var period = new ReportPeriod(March, March.AddDays(3));

        Assert.Equal([March, March.AddDays(1), March.AddDays(2), March.AddDays(3)], period.ClosingDays(BalanceInterval.Day));
    }

    [Fact]
    public void A_weekly_running_balance_has_a_balance_at_the_end_of_every_week_from_monday_to_sunday_and_at_the_end_of_the_period()
    {
        // 2026-03-01 is a Sunday; the Sundays after it are the 8th, 15th, 22nd and 29th.
        var march = new ReportPeriod(March, new DateOnly(2026, 3, 31));

        Assert.Equal(
            [March, new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 29), new DateOnly(2026, 3, 31)],
            march.ClosingDays(BalanceInterval.Week));
    }

    [Fact]
    public void A_period_inside_one_week_has_one_weekly_balance_at_its_end()
    {
        var wednesdayToFriday = new ReportPeriod(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));

        Assert.Equal([new DateOnly(2026, 3, 6)], wednesdayToFriday.ClosingDays(BalanceInterval.Week));
    }

    [Fact]
    public void The_balance_at_the_end_of_a_day_is_the_opening_balance_plus_every_transaction_dated_on_or_before_it()
    {
        var history = new BalanceHistory(
            OpeningBalance: 1000m, OpeningDate: new DateOnly(2026, 1, 1), NetBefore: -200m,
            NetOn: new Dictionary<DateOnly, decimal> { [March] = 50m, [March.AddDays(2)] = -30m });

        Assert.Equal([850m, 850m, 820m, 820m], history.BalancesOn([March, March.AddDays(1), March.AddDays(2), March.AddDays(3)]));
    }

    [Fact]
    public void Before_its_opening_date_an_account_holds_only_what_its_transactions_come_to()
    {
        var history = new BalanceHistory(
            OpeningBalance: 1000m, OpeningDate: March.AddDays(2), NetBefore: 0m, NetOn: new Dictionary<DateOnly, decimal> { [March.AddDays(3)] = -100m });

        Assert.Equal([0m, 0m, 1000m, 900m], history.BalancesOn([March, March.AddDays(1), March.AddDays(2), March.AddDays(3)]));
    }

    [Fact]
    public void A_weekly_balance_counts_every_day_of_the_week_up_to_its_end()
    {
        var history = new BalanceHistory(
            OpeningBalance: 0m, OpeningDate: new DateOnly(2026, 1, 1), NetBefore: 10m,
            NetOn: new Dictionary<DateOnly, decimal> { [March.AddDays(1)] = 1m, [March.AddDays(3)] = 2m, [March.AddDays(8)] = 4m });

        Assert.Equal([10m, 13m, 17m], history.BalancesOn([March, March.AddDays(7), March.AddDays(14)]));
    }
}
