using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>A recurring rule as the use cases return it.</summary>
/// <param name="Amount">Greater than zero; the type gives the sign.</param>
/// <param name="Currency">The currency of the rule's account.</param>
/// <param name="Interval">The N of an every N days rule; null for every other frequency.</param>
/// <param name="NextRunDate">The due date of the next occurrence to post; null once the rule has ended.</param>
/// <param name="RowVersion">The version an edit must name. Posting an occurrence, pausing and resuming change it too.</param>
public sealed record RecurringRuleDetails(
    Guid Id, TransactionType Type, Guid AccountId, decimal Amount, Currency Currency, Guid CategoryId, string? Payee, string? Note,
    RecurringFrequency Frequency, int? Interval, DateOnly StartDate, DateOnly? EndDate, DateOnly? NextRunDate, bool IsPaused, RecurringMode Mode, uint RowVersion)
{
    public static RecurringRuleDetails Of(RecurringRule rule, Currency currency) =>
        new(rule.Id, rule.Type, rule.AccountId, rule.Amount, currency, rule.CategoryId, rule.Payee, rule.Note, rule.Frequency,
            rule.Frequency == RecurringFrequency.EveryNDays ? rule.Interval : null, rule.StartDate, rule.EndDate, rule.NextRunDate, rule.IsPaused,
            rule.Mode, rule.RowVersion);
}
