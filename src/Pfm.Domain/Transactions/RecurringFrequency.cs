namespace Pfm.Domain.Transactions;

/// <summary>How often a recurring rule is due (spec REC-1). No frequency is zero, so an unset one is never valid.</summary>
public enum RecurringFrequency
{
    /// <summary>On the weekday of the start date, every week.</summary>
    Weekly = 1,

    /// <summary>On the day of the month of the start date, or the last day of a month that is shorter.</summary>
    Monthly = 2,

    /// <summary>On the start date's day and month; 29 February is the 28th in a year that has none.</summary>
    Yearly = 3,

    /// <summary>Every so many days, counted from the start date: the rule's interval.</summary>
    EveryNDays = 4,
}
