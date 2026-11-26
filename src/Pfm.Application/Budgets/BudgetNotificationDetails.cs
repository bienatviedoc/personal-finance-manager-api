using Pfm.Domain;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>A budget notification as the use cases return it, with the category it is about.</summary>
/// <param name="State">The state the budget moved into: warning or over.</param>
/// <param name="Budgeted">The budget's amount when it was raised.</param>
/// <param name="Spent">What was spent when it was raised.</param>
/// <param name="PercentSpent">Spent as whole percent of the budget then, rounded down; null for a budget of zero.</param>
/// <param name="ReadAt">Null while unread.</param>
public sealed record BudgetNotificationDetails(
    Guid Id, BudgetMonth Month, BudgetCategoryDetails Category, BudgetState State, decimal Budgeted, decimal Spent, int? PercentSpent, Currency Currency,
    DateTimeOffset RaisedAt, DateTimeOffset? ReadAt)
{
    internal static BudgetNotificationDetails Of(BudgetNotification notification, BudgetCategoryDetails category) =>
        new(notification.Id, notification.Month, category, notification.State, notification.Budgeted, notification.Spent, notification.PercentSpent,
            notification.Currency, notification.RaisedAt, notification.ReadAt);

    /// <summary>The category as it is now, or as little as is known of one the user no longer has.</summary>
    internal static BudgetCategoryDetails CategoryIn(CategoryTree tree, Guid categoryId) =>
        tree.Find(categoryId) is { } category
            ? new BudgetCategoryDetails(category.Id, category.ParentId, category.Name, category.Icon, category.Color, category.IsArchived)
            : new BudgetCategoryDetails(categoryId, null, "", "", "#64748b", true);
}

/// <summary>A page of the user's budget notifications, the newest first.</summary>
/// <param name="Unread">How many of all of them, on any page, are unread.</param>
public sealed record BudgetNotificationPage(IReadOnlyList<BudgetNotificationDetails> Items, string? NextCursor, int Unread);
