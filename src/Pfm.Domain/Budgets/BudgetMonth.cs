using System.Globalization;

namespace Pfm.Domain.Budgets;

/// <summary>
/// The calendar month, from the 1st, that a budget applies to. Written <c>2026-11</c>. A transaction is in the
/// budget month of its date, which is a calendar date in the user's time zone.
/// </summary>
public readonly record struct BudgetMonth
{
    private const string Format = "yyyy-MM";

    public BudgetMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, DateOnly.MinValue.Year);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, DateOnly.MaxValue.Year);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        FirstDay = new DateOnly(year, month, 1);
    }

    /// <summary>The 1st of the month. A default budget month is January of the year 1.</summary>
    public DateOnly FirstDay { get; }

    public DateOnly LastDay => new(FirstDay.Year, FirstDay.Month, DateTime.DaysInMonth(FirstDay.Year, FirstDay.Month));

    /// <summary>The budget month a calendar date is in.</summary>
    public static BudgetMonth Of(DateOnly date) => new(date.Year, date.Month);

    /// <summary>
    /// The budget months from the one &lt;paramref name="from"/&gt; is in to the one &lt;paramref name="to"/&gt; is in, in order.
    /// </summary>
    public static IReadOnlyList<BudgetMonth> Spanning(DateOnly from, DateOnly to)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        var months = new List<BudgetMonth>();
        for (var month = Of(from); month.FirstDay <= to; month = Of(month.FirstDay.AddMonths(1)))
        {
            months.Add(month);
        }

        return months;
    }

    /// <summary>Reads a budget month written as a four-digit year and a two-digit month: <c>2026-11</c>.</summary>
    public static bool TryParse(string? text, out BudgetMonth month)
    {
        var parsed = DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstDay);
        month = parsed ? Of(firstDay) : default;
        return parsed;
    }

    public override string ToString() => FirstDay.ToString(Format, CultureInfo.InvariantCulture);
}
