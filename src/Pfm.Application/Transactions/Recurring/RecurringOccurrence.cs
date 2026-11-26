using System.Globalization;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>One occurrence of a recurring rule: what its transaction will say, and the date it is due on.</summary>
/// <param name="Amount">Greater than zero, in <paramref name="Currency"/>; the type gives the sign.</param>
/// <param name="Currency">The currency of the rule's account.</param>
public sealed record OccurrenceDetails(
    Guid RecurringRuleId, DateOnly DueDate, TransactionType Type, Guid AccountId, decimal Amount, Currency Currency, Guid CategoryId, string? Payee,
    string? Note, RecurringMode Mode)
{
    public static OccurrenceDetails Of(RecurringRuleDetails rule, DateOnly dueDate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new(rule.Id, dueDate, rule.Type, rule.AccountId, rule.Amount, rule.Currency, rule.CategoryId, rule.Payee, rule.Note, rule.Mode);
    }
}

/// <summary>The occurrences of a recurring rule as transactions.</summary>
internal static class RecurringOccurrence
{
    /// <summary>
    /// The transaction a user would record for the occurrence due on <paramref name="dueDate"/>, as they would
    /// send it, under an idempotency key made of the rule and the due date, so an occurrence never has two.
    /// </summary>
    /// <param name="amount">The amount as sent, when the user adjusted it; null for the rule's own.</param>
    public static CreateTransactionCommand TransactionOf(RecurringRule rule, DateOnly dueDate, string? amount)
    {
        var date = DateText.Of(dueDate);
        return new CreateTransactionCommand(
            EnumText.Of(rule.Type), rule.AccountId, amount ?? rule.Amount.ToString(CultureInfo.InvariantCulture), date,
            rule.CategoryId, rule.Payee, rule.Note, IdempotencyKey: string.Create(CultureInfo.InvariantCulture, $"recurring-rule:{rule.Id:N}:{date}"),
            RecurringRuleId: rule.Id);
    }

    /// <summary>The occurrences these rules have on the dates <paramref name="dueDates"/> picks, earliest first, then by rule.</summary>
    public static async Task<IReadOnlyList<OccurrenceDetails>> OfAsync(
        IEnumerable<RecurringRule> rules, Func<RecurringRule, IEnumerable<DateOnly>> dueDates, RecurringRuleContext context,
        CancellationToken cancellationToken)
    {
        var occurrences = new List<OccurrenceDetails>();
        foreach (var rule in rules)
        {
            var dates = dueDates(rule).ToList();
            if (dates.Count == 0)
            {
                continue;
            }

            var details = await context.DetailsAsync(rule, cancellationToken);
            occurrences.AddRange(dates.Select(dueDate => OccurrenceDetails.Of(details, dueDate)));
        }

        return [.. occurrences.OrderBy(occurrence => occurrence.DueDate).ThenBy(occurrence => occurrence.RecurringRuleId)];
    }
}
