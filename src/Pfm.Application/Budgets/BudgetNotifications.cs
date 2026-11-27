using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>
/// Raises the budget notifications a change made due (spec BUD-7). It reads each budget month a change touched
/// as the budget screen does, and raises a notification for each category whose budget moved into warning or over
/// and has not raised that one this month (<see cref="BudgetNotification.Due"/>). The overall budget raises none: BUD-7
/// is about a category, and the month read holds the overall budget apart from its category budgets. A unique index holds that when
/// two requests raise the same one at once: the second reads again. New notifications are emailed unless the user
/// switched that off.
/// Only this budget month and later ones are checked: a month that has ended is history, and it may hold budgets
/// that went over before notifications existed, so a late edit or a merge touching it must not notify about all of them.
/// </summary>
public sealed class BudgetNotifications(
    BudgetMonthReader reader, IBudgetNotificationRepository notifications, ICategoryLookup categories, INotificationRecipient recipients,
    IBudgetNotificationEmails emails, IUserCalendar calendar, IUnitOfWork unitOfWork, TimeProvider clock)
    : IBudgetNotifications
{
    public async Task CheckAsync(IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var thisMonth = BudgetMonth.Of(await calendar.TodayAsync(cancellationToken));
        List<BudgetNotification> raised = [];
        foreach (var month in dates.Select(BudgetMonth.Of).Distinct().Where(month => month.FirstDay >= thisMonth.FirstDay))
        {
            raised.AddRange(await RaiseAsync(month, cancellationToken));
        }

        if (raised.Count == 0 || await recipients.OfCurrentUserAsync(cancellationToken) is not { EmailBudgetNotifications: true } recipient)
        {
            return;
        }

        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        foreach (var notification in raised)
        {
            await emails.SendAsync(
                recipient, BudgetNotificationDetails.Of(notification, BudgetNotificationDetails.CategoryIn(tree, notification.CategoryId)), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<BudgetNotification>> RaiseAsync(BudgetMonth month, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var read = await reader.ReadAsync(month, cancellationToken);
            if (read.Items.Count == 0)
            {
                return [];
            }

            var already = (await notifications.ListForMonthAsync(month, cancellationToken)).ToLookup(notification => notification.CategoryId, notification => notification.State);
            List<BudgetNotification> raised = [];
            foreach (var budget in read.Items)
            {
                if (BudgetNotification.Due(budget.State, [.. already[budget.CategoryId]]) is { } state)
                {
                    var notification = BudgetNotification.Raise(budget.CategoryId, month, state, budget.Budgeted, budget.Spent, read.Currency, clock.GetUtcNow());
                    notifications.Add(notification);
                    raised.Add(notification);
                }
            }

            if (raised.Count == 0)
            {
                return [];
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return raised;
            }
            catch (AlreadyExistsException)
            {
                // Another request raised one of them first: read again what is raised now.
                raised.ForEach(notifications.Forget);
                if (attempt >= ConcurrentChanges.Attempts)
                {
                    return [];
                }
            }
        }
    }
}
