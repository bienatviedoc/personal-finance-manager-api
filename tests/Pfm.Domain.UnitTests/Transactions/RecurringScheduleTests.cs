using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

/// <summary>The date arithmetic of a recurring rule: which calendar dates its occurrences are due on.</summary>
public class RecurringScheduleTests
{
    [Fact]
    public void The_first_occurrence_is_due_on_the_start_date()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-05"), null);

        Assert.Equal(Date("2026-11-05"), schedule.FirstAfter(null));
        Assert.Equal(Date("2026-11-05"), schedule.FirstAfter(Date("2026-10-01")));
    }

    [Fact]
    public void A_weekly_rule_is_due_on_the_same_weekday_every_week()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-11-02"), null);

        Assert.Equal(Dates("2026-11-02", "2026-11-09", "2026-11-16", "2026-11-23"), schedule.Through(Date("2026-11-29")));
    }

    [Fact]
    public void A_weekly_rule_carries_on_over_the_end_of_a_year()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-12-24"), null);

        Assert.Equal(Dates("2026-12-24", "2026-12-31", "2027-01-07"), schedule.Through(Date("2027-01-10")));
    }

    [Fact]
    public void An_every_N_days_rule_is_due_N_days_after_the_occurrence_before()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.EveryNDays, 10, Date("2028-02-09"), null);

        // 2028 is a leap year: ten days after 19 February is the 29th.
        Assert.Equal(Dates("2028-02-09", "2028-02-19", "2028-02-29", "2028-03-10"), schedule.Through(Date("2028-03-15")));
    }

    [Fact]
    public void A_monthly_rule_is_due_on_the_same_day_of_every_month()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-15"), null);

        Assert.Equal(Dates("2026-11-15", "2026-12-15", "2027-01-15", "2027-02-15"), schedule.Through(Date("2027-03-01")));
    }

    [Fact]
    public void A_monthly_rule_started_on_the_31st_is_due_on_the_last_day_of_a_shorter_month_and_on_the_31st_again_after_it()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2027-01-31"), null);

        Assert.Equal(
            Dates("2027-01-31", "2027-02-28", "2027-03-31", "2027-04-30", "2027-05-31"),
            schedule.Through(Date("2027-06-01")));
    }

    [Fact]
    public void A_monthly_rule_started_on_the_30th_is_due_on_29_February_in_a_leap_year_and_the_28th_in_any_other()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2027-01-30"), null);

        Assert.Contains(Date("2027-02-28"), schedule.Through(Date("2027-03-01")));
        Assert.Contains(Date("2028-02-29"), schedule.Through(Date("2028-03-01")));
        Assert.Contains(Date("2028-03-30"), schedule.Through(Date("2028-04-01")));
    }

    [Fact]
    public void A_yearly_rule_is_due_on_the_same_date_every_year()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Yearly, null, Date("2026-03-14"), null);

        Assert.Equal(Dates("2026-03-14", "2027-03-14", "2028-03-14"), schedule.Through(Date("2028-12-31")));
    }

    [Fact]
    public void A_yearly_rule_started_on_29_February_is_due_on_the_28th_in_other_years_and_the_29th_in_the_next_leap_year()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Yearly, null, Date("2024-02-29"), null);

        Assert.Equal(
            Dates("2024-02-29", "2025-02-28", "2026-02-28", "2027-02-28", "2028-02-29"),
            schedule.Through(Date("2028-12-31")));
    }

    [Theory]
    [InlineData(RecurringFrequency.Weekly, null, "2026-11-02", "2026-11-02", "2026-11-09")]
    [InlineData(RecurringFrequency.Weekly, null, "2026-11-02", "2026-11-08", "2026-11-09")]
    [InlineData(RecurringFrequency.Weekly, null, "2026-11-02", "2026-11-09", "2026-11-16")]
    [InlineData(RecurringFrequency.EveryNDays, 3, "2026-11-02", "2026-11-04", "2026-11-05")]
    [InlineData(RecurringFrequency.EveryNDays, 3, "2026-11-02", "2026-11-05", "2026-11-08")]
    [InlineData(RecurringFrequency.Monthly, null, "2027-01-31", "2027-02-27", "2027-02-28")]
    [InlineData(RecurringFrequency.Monthly, null, "2027-01-31", "2027-02-28", "2027-03-31")]
    [InlineData(RecurringFrequency.Monthly, null, "2027-01-31", "2029-12-31", "2030-01-31")]
    [InlineData(RecurringFrequency.Yearly, null, "2024-02-29", "2027-02-28", "2028-02-29")]
    [InlineData(RecurringFrequency.Yearly, null, "2024-02-29", "2028-02-28", "2028-02-29")]
    public void The_occurrence_after_a_date_is_the_first_one_later_than_it_however_long_ago_the_rule_started(
        RecurringFrequency frequency, int? interval, string start, string after, string expected)
    {
        var schedule = RecurringSchedule.Of(frequency, interval, Date(start), null);

        Assert.Equal(Date(expected), schedule.FirstAfter(Date(after)));
    }

    [Fact]
    public void A_rule_has_no_occurrence_after_its_end_date_and_one_on_it()
    {
        var schedule = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-15"), Date("2027-01-15"));

        Assert.Equal(Dates("2026-11-15", "2026-12-15", "2027-01-15"), schedule.Through(Date("2030-01-01")));
        Assert.Null(schedule.FirstAfter(Date("2027-01-15")));
    }

    [Fact]
    public void A_rule_cannot_end_before_it_starts_but_can_end_the_day_it_starts()
    {
        var violation = Assert.Throws<RecurringRuleViolation>(() =>
            RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-11-15"), Date("2026-11-14")));

        Assert.Equal(nameof(RecurringSchedule.EndDate), violation.Property);
        Assert.Equal(Dates("2026-11-15"), RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-11-15"), Date("2026-11-15")).Through(Date("2027-01-01")));
    }

    [Theory]
    [InlineData(RecurringFrequency.EveryNDays, null)]
    [InlineData(RecurringFrequency.EveryNDays, 0)]
    [InlineData(RecurringFrequency.EveryNDays, 366)]
    [InlineData(RecurringFrequency.Monthly, 2)]
    public void Only_an_every_N_days_rule_takes_an_interval_and_it_is_1_to_365_days(RecurringFrequency frequency, int? interval)
    {
        var violation = Assert.Throws<RecurringRuleViolation>(() => RecurringSchedule.Of(frequency, interval, Date("2026-11-15"), null));

        Assert.Equal(nameof(RecurringSchedule.Interval), violation.Property);
    }

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DateOnly[] Dates(params string[] texts) => [.. texts.Select(Date)];
}
