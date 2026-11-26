using System.Diagnostics.CodeAnalysis;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Budgets;

/// <summary>The user's budget notifications, the newest first, a page at a time, with how many are unread.</summary>
/// <param name="After">Where the page before ended.</param>
public sealed record ListBudgetNotifications(int Limit = Paging.DefaultLimit, BudgetNotificationCursor? After = null);

public sealed class ListBudgetNotificationsHandler(IBudgetNotificationRepository notifications, ICategoryLookup categories) : IQueryHandler
{
    public async Task<BudgetNotificationPage> HandleAsync(ListBudgetNotifications query, CancellationToken cancellationToken = default)
    {
        // One more than the page holds says whether another page follows.
        var found = await notifications.ListNewestFirstAsync(query.Limit + 1, query.After?.Id, cancellationToken);
        var page = found.Take(query.Limit).ToList();
        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        return new BudgetNotificationPage(
            [.. page.Select(notification => BudgetNotificationDetails.Of(notification, BudgetNotificationDetails.CategoryIn(tree, notification.CategoryId)))],
            found.Count > query.Limit ? new BudgetNotificationCursor(page[^1].Id).ToString() : null,
            await notifications.CountUnreadAsync(cancellationToken));
    }
}

/// <summary>Where a page of budget notifications ends: the id of its last one, opaque to clients.</summary>
public sealed record BudgetNotificationCursor(Guid Id)
{
    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out BudgetNotificationCursor? parsed)
    {
        parsed = null;
        if (!CursorText.TryDecode(cursor, out var text) || !Guid.TryParseExact(text, "N", out var id))
        {
            return false;
        }

        parsed = new BudgetNotificationCursor(id);
        return true;
    }

    public override string ToString() => CursorText.Encode(Id.ToString("N"));
}
