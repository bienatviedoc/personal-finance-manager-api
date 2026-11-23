namespace Pfm.Domain.Transactions;

/// <summary>
/// When a recurring rule is due: on its start date and then at its frequency, until its end date if it has
/// one. Every occurrence is worked out from the start date, never from the occurrence before, so a monthly
/// rule started on the 31st is due on the last day of February and on the 31st again in March.
/// </summary>
public readonly record struct RecurringSchedule
{
    /// <summary>The longest an every N days rule waits between occurrences.</summary>
    public const int MaxInterval = 365;

    private RecurringSchedule(RecurringFrequency frequency, int interval, DateOnly startDate, DateOnly? endDate)
    {
        Frequency = frequency;
        Interval = interval;
        StartDate = startDate;
        EndDate = endDate;
    }

    public RecurringFrequency Frequency { get; }

    /// <summary>The N of an every N days rule; 1 for every other frequency.</summary>
    public int Interval { get; }

    /// <summary>The date the first occurrence is due on.</summary>
    public DateOnly StartDate { get; }

    /// <summary>The last date an occurrence can be due on; null for a rule that goes on.</summary>
    public DateOnly? EndDate { get; }

    public static bool IsValidInterval(RecurringFrequency frequency, int? interval) =>
        frequency == RecurringFrequency.EveryNDays ? interval is >= 1 and <= MaxInterval : interval is null;

    /// <param name="interval">The N of an every N days rule, 1 to <see cref="MaxInterval"/>; null for every other frequency.</param>
    /// <exception cref="RecurringRuleViolation">The interval does not go with the frequency, or the end date is before the start date.</exception>
    public static RecurringSchedule Of(RecurringFrequency frequency, int? interval, DateOnly startDate, DateOnly? endDate)
    {
        if (!Enum.IsDefined(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Not a frequency.");
        }

        if (!IsValidInterval(frequency, interval))
        {
            throw new RecurringRuleViolation(
                nameof(Interval),
                frequency == RecurringFrequency.EveryNDays ? $"An every N days rule is due every 1 to {MaxInterval} days." : "Only an every N days rule takes an interval.");
        }

        if (endDate < startDate)
        {
            throw new RecurringRuleViolation(nameof(EndDate), "A recurring rule cannot end before it starts.");
        }

        return new RecurringSchedule(frequency, interval ?? 1, startDate, endDate);
    }

    /// <summary>
    /// The date of the first occurrence later than <paramref name="date"/>, or of the very first one when
    /// <paramref name="date"/> is null; null when the rule has ended by then.
    /// </summary>
    public DateOnly? FirstAfter(DateOnly? date)
    {
        var next = date is { } after && after >= StartDate ? Occurrence(FirstIndexAfter(after)) : StartDate;
        return next > EndDate ? null : next;
    }

    /// <summary>Every occurrence due on or before <paramref name="date"/>, earliest first.</summary>
    public IEnumerable<DateOnly> Through(DateOnly date)
    {
        for (var next = FirstAfter(null); next <= date; next = FirstAfter(next))
        {
            yield return next.Value;
        }
    }

    /// <summary>The date of the occurrence <paramref name="index"/> steps after the first.</summary>
    private DateOnly Occurrence(int index) => Frequency switch
    {
        RecurringFrequency.Weekly => StartDate.AddDays(7 * index),
        RecurringFrequency.EveryNDays => StartDate.AddDays(Interval * index),
        // AddMonths and AddYears keep the day of the start date where the month has it, and take its last day where it does not.
        RecurringFrequency.Monthly => StartDate.AddMonths(index),
        RecurringFrequency.Yearly => StartDate.AddYears(index),
        _ => throw new InvalidOperationException("Not a frequency."),
    };

    /// <param name="after">A date on or after the start date.</param>
    private int FirstIndexAfter(DateOnly after)
    {
        var daysSinceStart = after.DayNumber - StartDate.DayNumber;
        switch (Frequency)
        {
            case RecurringFrequency.Weekly:
                return (daysSinceStart / 7) + 1;
            case RecurringFrequency.EveryNDays:
                return (daysSinceStart / Interval) + 1;
            default:
                // The occurrence in the month (or year) of the date: the one after it when that one is not later than the date.
                var index = Frequency == RecurringFrequency.Monthly
                    ? ((after.Year - StartDate.Year) * 12) + after.Month - StartDate.Month
                    : after.Year - StartDate.Year;
                return Occurrence(index) > after ? index : index + 1;
        }
    }
}
