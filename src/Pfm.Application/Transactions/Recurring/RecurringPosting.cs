using System.Globalization;
using System.Text.Json;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>What a run of posting came to.</summary>
/// <param name="Occurrences">How many occurrences have their transaction now.</param>
/// <param name="RulesNotPosted">
/// How many rules have an occurrence that was refused as a transaction: their account or category is
/// archived, or the amount no longer fits the account's currency. They wait, still due, until the user
/// changes the rule; every other rule was posted.
/// </param>
public sealed record OccurrencesPosted(int Occurrences, int RulesNotPosted);

/// <summary>
/// Posts every occurrence of a recurring rule that is due, as a transaction dated on its due date in the
/// user's time zone (spec REC-2). The worker runs it every few minutes; no user can. An occurrence missed
/// while the worker was down is posted by the next run, with its original due date. Nothing is posted for a
/// deactivated user. Each occurrence is recorded by
/// <see cref="CreateTransactionHandler"/>, so it is held to the rules of a transaction a user records.
/// </summary>
public sealed class RecurringPosting(
    IRecurringRuleRepository rules, CreateTransactionHandler recordTransaction, IUserCalendar calendar, IUserDeactivation deactivation,
    IActForUser actFor, IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>Runs for no user, so it acts for each user who may have something due in turn.</summary>
    public async Task<OccurrencesPosted> PostDueAsync(CancellationToken cancellationToken = default)
    {
        // Somewhere it is already tomorrow by UTC's calendar: ask for everyone who could have something due, then go by each user's own today.
        var latestToday = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);
        var (occurrences, rulesNotPosted) = (0, 0);
        foreach (var userId in await rules.UsersWithDueRulesAsync(latestToday, cancellationToken))
        {
            // A deactivated user's data is left as it is. When they cancel the deletion, what was due meanwhile is
            // posted like any other missed occurrence.
            if (await deactivation.IsDeactivatedAsync(userId, cancellationToken))
            {
                continue;
            }

            using var _ = actFor.ActFor(userId);
            var today = await calendar.TodayAsync(cancellationToken);
            foreach (var ruleId in await rules.ListDueAsync(today, cancellationToken))
            {
                var (posted, refused) = await PostAsync(ruleId, today, cancellationToken);
                occurrences += posted;
                rulesNotPosted += refused ? 1 : 0;
            }

            // The next user's rows are read on their own.
            unitOfWork.DiscardChanges();
        }

        return new OccurrencesPosted(occurrences, rulesNotPosted);
    }

    /// <summary>
    /// Posts the rule's occurrences one at a time, earliest first, until none is due. The transaction is saved
    /// first, under an idempotency key made of the rule and the due date, and then the rule moves on: whatever
    /// stops in between, or runs at the same moment, the next attempt finds the transaction and records no second one.
    /// </summary>
    private async Task<(int Posted, bool Refused)> PostAsync(Guid ruleId, DateOnly today, CancellationToken cancellationToken)
    {
        for (var posted = 0; ;)
        {
            // Read again each time: the user may have edited, paused or deleted it, or another worker moved it on.
            var rule = await rules.FindAsync(ruleId, cancellationToken);
            if (rule is not { IsPaused: false, NextRunDate: { } dueDate } || dueDate > today)
            {
                return (posted, false);
            }

            var recorded = await recordTransaction.HandleAsync(OccurrenceOf(rule, dueDate), cancellationToken);
            if (!recorded.IsValid)
            {
                return (posted, true);
            }

            // Recording may have met another worker posting the same occurrence, and left behind what it read and
            // the transaction it did not save: forget all of it, and read the rule as it is saved now.
            unitOfWork.DiscardChanges();
            rule = await rules.FindAsync(ruleId, cancellationToken);
            if (rule?.NextRunDate != dueDate)
            {
                continue;
            }

            try
            {
                rule.Posted(dueDate, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                posted++;
            }
            catch (ConcurrentChangeException)
            {
                unitOfWork.DiscardChanges();
            }
        }
    }

    /// <summary>The transaction a user would record for this occurrence, as they would send it.</summary>
    private static CreateTransactionCommand OccurrenceOf(RecurringRule rule, DateOnly dueDate)
    {
        var date = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new CreateTransactionCommand(
            JsonNamingPolicy.CamelCase.ConvertName(rule.Type.ToString()), rule.AccountId, rule.Amount.ToString(CultureInfo.InvariantCulture), date,
            rule.CategoryId, rule.Payee, rule.Note, IdempotencyKey: string.Create(CultureInfo.InvariantCulture, $"recurring-rule:{rule.Id:N}:{date}"), RecurringRuleId: rule.Id);
    }
}
