using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Budgets;

/// <summary>The user's budget notifications, the newest first, a page at a time, with how many are unread.</summary>
/// <param name="After">The id of the last notification of the page before.</param>
public sealed record ListBudgetNotifications(int Limit = Paging.DefaultLimit, Guid? After = null);

public sealed class ListBudgetNotificationsHandler(IBudgetNotificationRepository notifications, ICategoryLookup categories) : IQueryHandler
{
    public async Task<BudgetNotificationPage> HandleAsync(ListBudgetNotifications query, CancellationToken cancellationToken = default)
    {
        var found = await notifications.ListNewestFirstAsync(query.Limit + 1, query.After, cancellationToken);
        var page = found.Take(query.Limit).ToList();
        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        return new BudgetNotificationPage(
            [.. page.Select(notification => BudgetNotificationDetails.Of(notification, BudgetNotificationDetails.CategoryIn(tree, notification.CategoryId)))],
            found.Count > query.Limit ? page[^1].Id.ToString("N") : null,
            await notifications.CountUnreadAsync(cancellationToken));
    }
}
