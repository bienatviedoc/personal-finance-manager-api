using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// The user's reminders: every occurrence of a rule that reminds, due on or before today in their time zone,
/// that they have not confirmed or dismissed yet (spec REC-2). A paused rule has none.
/// </summary>
public sealed record ListReminders;

public sealed class ListRemindersHandler(IRecurringRuleRepository rules, RecurringRuleContext context, IUserCalendar calendar) : IQueryHandler
{
    /// <summary>The reminders, earliest due first.</summary>
    public async Task<IReadOnlyList<OccurrenceDetails>> HandleAsync(ListReminders query, CancellationToken cancellationToken = default)
    {
        var today = await calendar.TodayAsync(cancellationToken);
        return await RecurringOccurrence.OfAsync(
            await rules.ListUnsettledThroughAsync(today, cancellationToken), rule => rule.Reminders(today), context, cancellationToken);
    }
}
