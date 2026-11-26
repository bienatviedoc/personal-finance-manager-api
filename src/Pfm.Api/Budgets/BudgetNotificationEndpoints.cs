using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Budgets;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Budgets;

namespace Pfm.Api.Budgets;

/// <summary>Budget notifications over HTTP (spec BUD-7). The use cases decide everything; these only map requests and answers.</summary>
public static class BudgetNotificationEndpoints
{
    public static IEndpointRouteBuilder MapBudgetNotificationEndpoints(this IEndpointRouteBuilder api)
    {
        var notifications = api.MapGroup("/budget-notifications").WithTags("Budgets").ProducesProblem(StatusCodes.Status401Unauthorized);

        notifications.MapGet("/", List).WithName("ListBudgetNotifications")
            .WithDescription("The user's budget notifications, the newest first, a page at a time, with how many are unread. One is raised when a category's budget moves into warning or over after a change that affects spent; each category raises at most one warning and one over a budget month. Each keeps what the budget stood at when it was raised.");
        notifications.MapPost("/{id:guid}/read", Read).WithName("ReadBudgetNotification")
            .WithDescription("Marks one budget notification read. Reading one already read changes nothing.");
        notifications.MapPost("/read", ReadAll).WithName("ReadAllBudgetNotifications")
            .WithDescription("Marks every unread budget notification read.");

        return api;
    }

    /// <param name="limit">How many notifications a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<BudgetNotificationListResponse>, ValidationProblem>> List(
        ListBudgetNotificationsHandler handler, CancellationToken cancellationToken, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        Guid? after = null;
        if (cursor is not null)
        {
            if (!Guid.TryParseExact(cursor, "N", out var id))
            {
                return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
            }

            after = id;
        }

        var page = await handler.HandleAsync(new ListBudgetNotifications(limit, after), cancellationToken);
        return TypedResults.Ok(new BudgetNotificationListResponse([.. page.Items.Select(BudgetNotificationResponse.From)], page.NextCursor, page.Unread));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Read(Guid id, ReadBudgetNotificationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ReadBudgetNotificationCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<NoContent> ReadAll(ReadAllBudgetNotificationsHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ReadAllBudgetNotificationsCommand(), cancellationToken);
        return TypedResults.NoContent();
    }
}

/// <summary>A page of budget notifications, the newest first.</summary>
/// <param name="NextCursor">Pass it as <c>cursor</c> for the next page; null on the last.</param>
/// <param name="Unread">How many of all the user's notifications, on any page, are unread.</param>
public sealed record BudgetNotificationListResponse(IReadOnlyList<BudgetNotificationResponse> Items, string? NextCursor, int Unread);

/// <summary>
/// Tells the user a category's budget moved into warning or over in a budget month. Amounts are decimal strings in
/// the user's base currency, as the budget stood when it was raised.
/// </summary>
/// <param name="Month">The budget month, as <c>2026-11</c>.</param>
/// <param name="Category">The category the budget is on, as it is now.</param>
/// <param name="State"><c>warning</c> or <c>over</c>: the state the budget moved into.</param>
/// <param name="Budgeted">The budget's amount when it was raised.</param>
/// <param name="Spent">What was spent when it was raised.</param>
/// <param name="PercentSpent">Spent as whole percent of the budget then, rounded down; null for a budget of zero.</param>
/// <param name="ReadAt">When the user read it; null while unread.</param>
public sealed record BudgetNotificationResponse(
    Guid Id, string Month, BudgetCategoryResponse Category, BudgetState State, string Budgeted, string Spent, int? PercentSpent, Currency Currency,
    DateTimeOffset RaisedAt, DateTimeOffset? ReadAt)
{
    public static BudgetNotificationResponse From(BudgetNotificationDetails notification) =>
        new(notification.Id, notification.Month.ToString(), BudgetCategoryResponse.From(notification.Category), notification.State,
            AmountJson.Text(notification.Budgeted), AmountJson.Text(notification.Spent), notification.PercentSpent, notification.Currency,
            notification.RaisedAt, notification.ReadAt);
}
