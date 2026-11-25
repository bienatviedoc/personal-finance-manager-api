namespace Pfm.Domain.Reports;

/// <summary>
/// The dates a report covers, from <see cref="From"/> to <see cref="To"/>, both included (spec section 7). They
/// are calendar dates in the user's time zone, like a transaction's date.
/// </summary>
public sealed record ReportPeriod
{
    /// <summary>
    /// The earliest date a report may be asked to start on. The period it is compared with starts earlier, and
    /// must still be on the calendar.
    /// </summary>
    public static readonly DateOnly Earliest = new(1900, 1, 1);

    public ReportPeriod(DateOnly from, DateOnly to)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    /// <summary>How many days the period covers, both ends counted.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;

    /// <summary>
    /// The calendar months the period touches, whole: from the 1st of its first month to the last day of its last.
    /// A period of whole months is its own.
    /// </summary>
    public ReportPeriod WholeMonths =>
        new(new DateOnly(From.Year, From.Month, 1), new DateOnly(To.Year, To.Month, DateTime.DaysInMonth(To.Year, To.Month)));

    /// <summary>How many calendar months the period touches, the first and last counted even when only part of them is in it.</summary>
    public int Months => ((To.Year - From.Year) * 12) + To.Month - From.Month + 1;

    /// <summary>
    /// The period a report compares this one with (spec section 7, "compare with the previous period"). A period of
    /// whole calendar months, from a 1st to the last day of a month, is compared with as many whole months just
    /// before it, so March is compared with February and a year with the year before; any other period with as
    /// many days just before it.
    /// </summary>
    public ReportPeriod Previous
    {
        get
        {
            if (IsWholeMonths(out var months))
            {
                var first = From.AddMonths(-months);
                return new ReportPeriod(first, From.AddDays(-1));
            }

            return new ReportPeriod(From.AddDays(-Days), From.AddDays(-1));
        }
    }

    private bool IsWholeMonths(out int months)
    {
        months = ((To.Year - From.Year) * 12) + To.Month - From.Month + 1;
        return From.Day == 1 && To.Day == DateTime.DaysInMonth(To.Year, To.Month);
    }
}
