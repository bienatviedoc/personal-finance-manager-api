using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Every occurrence of the user's recurring rules due from today, in their time zone, to no more than
/// <paramref name="Days"/> days after it, whether it will be posted or be a reminder (spec REC-3). One due today
/// is listed until it is posted, and not at all when it is a reminder already. A paused rule has none.
/// </summary>
/// <param name="Days">How many days ahead to look, 1 to <see cref="MaxDays"/>.</param>
public sealed record ListUpcomingOccurrences(int Days = ListUpcomingOccurrences.DefaultDays)
{
    public const int DefaultDays = 30;

    /// <summary>The longest a cash-flow forecast looks ahead (spec RPT-9).</summary>
    public const int MaxDays = 90;

    public static bool IsValidDays(int days) => days is >= 1 and <= MaxDays;
}

public sealed class ListUpcomingOccurrencesHandler(IRecurringRuleRepository rules, RecurringRuleContext context, IUserCalendar calendar) : IQueryHandler
{
    /// <summary>The occurrences, earliest due first.</summary>
    public async Task<IReadOnlyList<OccurrenceDetails>> HandleAsync(ListUpcomingOccurrences query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = await calendar.TodayAsync(cancellationToken);
        return await RecurringOccurrence.OfAsync(
            await rules.ListUnsettledThroughAsync(today.AddDays(query.Days), cancellationToken), rule => rule.Upcoming(today, query.Days), context,
            cancellationToken);
    }
}
