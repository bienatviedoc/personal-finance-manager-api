using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Budgets;
using Pfm.Domain;
using Pfm.Domain.Budgets;

namespace Pfm.Api.Budgets;

/// <summary>Budgets over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class BudgetEndpoints
{
    /// <summary>What these endpoints change, as a problem about its row version names it.</summary>
    private const string What = "budget";

    private const string MonthMessage = "Enter a month as YYYY-MM.";

    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder api)
    {
        var budgets = api.MapGroup("/budgets").WithTags("Budgets").ProducesProblem(StatusCodes.Status401Unauthorized);

        budgets.MapGet("/", List).WithName("ListBudgets")
            .WithDescription("The budgets of one budget month (a calendar month from the 1st), in the user's base currency: budgeted, spent, remaining and state for each, with totals. Spent is worked out from the month's expenses every time, each as converted at the exchange rate of its own date; an expense in a subcategory counts toward a budget on its parent, and transfers and income never count. Without month, the month it is now in the user's time zone.");
        budgets.MapPut("/{month}/{categoryId:guid}", Set).WithName("SetBudget")
            .WithDescription("Sets the budget of an expense category for a month, in the user's base currency, which is locked from the first budget on. A category and its subcategory cannot both have a budget in the same month. Setting a new budget answers 201 and sends no If-Match; changing the amount of one that exists answers 200 and must send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        budgets.MapDelete("/{month}/{categoryId:guid}", Remove).WithName("RemoveBudget")
            .WithDescription("Removes the budget a category has in a month.");
        budgets.MapPut("/{month}/overall", SetOverall).WithName("SetOverallBudget")
            .WithDescription("Sets the overall budget of a month: a cap on all of its spending in the user's base currency, whether or not a category has a budget of its own, and independent of the category budgets. Setting a new one answers 201 and sends no If-Match; changing the amount of one that exists answers 200 and must send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        budgets.MapDelete("/{month}/overall", RemoveOverall).WithName("RemoveOverallBudget")
            .WithDescription("Removes the overall budget of a month. Its category budgets stay.");
        budgets.MapPost("/{month}/copy-from/{sourceMonth}", Copy).WithName("CopyBudgets")
            .WithDescription("Copies the budgets of the source month into the month in one action, such as last month's into this one, each with its amount. Its overall budget is copied too when the month has none.");

        return api;
    }

    /// <param name="month">The budget month, as <c>2026-11</c>; left out for the current month.</param>
    private static async Task<Results<Ok<BudgetMonthResponse>, ValidationProblem>> List(
        ListBudgetsHandler handler, CancellationToken cancellationToken, string? month = null)
    {
        BudgetMonth? asked = null;
        if (month is not null)
        {
            if (!BudgetMonth.TryParse(month, out var parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["month"] = [MonthMessage] });
            }

            asked = parsed;
        }

        return TypedResults.Ok(BudgetMonthResponse.From(await handler.HandleAsync(new ListBudgets(asked), cancellationToken)));
    }

    /// <param name="month">The budget month, as <c>2026-11</c>.</param>
    /// <param name="ifMatch">The budget's row version as last read, in double quotes; left out when the category has no budget that month yet.</param>
    private static async Task<Results<Ok<BudgetResponse>, Created<BudgetResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Set(
        string month, Guid categoryId, BudgetRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, SetBudgetHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!RowVersions.TryReadOptional(ifMatch, What, out var rowVersion, out var refused))
        {
            return refused;
        }

        var result = await handler.HandleAsync(new SetBudgetCommand(month, categoryId, request.Amount, rowVersion), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            SetBudgetOutcome.Created { Budget: var budget } => TypedResults.Created($"/api/v1/budgets/{month}/{categoryId}", Versioned(http, BudgetResponse.From(budget))),
            SetBudgetOutcome.Changed { Budget: var budget } => TypedResults.Ok(Versioned(http, BudgetResponse.From(budget))),
            SetBudgetOutcome.RowVersionRequired => RowVersions.Required(What),
            _ => RowVersions.Stale(What),
        };
    }

    /// <param name="month">The budget month, as <c>2026-11</c>.</param>
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Remove(
        string month, Guid categoryId, RemoveBudgetHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RemoveBudgetCommand(month, categoryId), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <param name="month">The budget month, as <c>2026-11</c>.</param>
    /// <param name="ifMatch">The overall budget's row version as last read, in double quotes; left out when the month has no overall budget yet.</param>
    private static async Task<Results<Ok<OverallBudgetResponse>, Created<OverallBudgetResponse>, ValidationProblem, ProblemHttpResult>> SetOverall(
        string month, BudgetRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, SetOverallBudgetHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!RowVersions.TryReadOptional(ifMatch, What, out var rowVersion, out var refused))
        {
            return refused;
        }

        var result = await handler.HandleAsync(new SetOverallBudgetCommand(month, request.Amount, rowVersion), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            SetOverallBudgetOutcome.Created { Budget: var budget } => TypedResults.Created($"/api/v1/budgets/{month}/overall", Versioned(http, OverallBudgetResponse.From(budget))),
            SetOverallBudgetOutcome.Changed { Budget: var budget } => TypedResults.Ok(Versioned(http, OverallBudgetResponse.From(budget))),
            SetOverallBudgetOutcome.RowVersionRequired => RowVersions.Required(What),
            _ => RowVersions.Stale(What),
        };
    }

    /// <param name="month">The budget month, as <c>2026-11</c>.</param>
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> RemoveOverall(
        string month, RemoveOverallBudgetHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RemoveOverallBudgetCommand(month), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <param name="month">The budget month copied into, as <c>2026-11</c>.</param>
    /// <param name="sourceMonth">The budget month copied from, as <c>2026-10</c>.</param>
    private static async Task<Results<Ok<CopiedBudgetsResponse>, ValidationProblem>> Copy(
        string month, string sourceMonth, CopyBudgetsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CopyBudgetsCommand(month, sourceMonth), cancellationToken);
        return result.IsValid ? TypedResults.Ok(new CopiedBudgetsResponse(result.Outcome!.Copied)) : ValidationProblems.For(result);
    }

    private static BudgetResponse Versioned(HttpContext http, BudgetResponse budget)
    {
        http.SendAsETag(budget.RowVersion);
        return budget;
    }

    private static OverallBudgetResponse Versioned(HttpContext http, OverallBudgetResponse budget)
    {
        http.SendAsETag(budget.RowVersion);
        return budget;
    }
}

/// <summary>What setting a budget sends. Its currency is not sent: it is the user's base currency.</summary>
/// <param name="Amount">A decimal string of zero or more, such as <c>"3000000.00"</c>, with no more decimals than the base currency has.</param>
public sealed record BudgetRequest(string Amount);

/// <summary>What copying the budgets of one month into another came to.</summary>
/// <param name="Copied">How many budgets the month gained.</param>
public sealed record CopiedBudgetsResponse(int Copied);

/// <summary>A budget against what was spent in its month. Amounts are decimal strings in the user's base currency.</summary>
/// <param name="Category">The category the budget is on.</param>
/// <param name="Budgeted">The budget's amount.</param>
/// <param name="Spent">The month's expenses in the category and its subcategories. Derived from transactions, and negative when more came back than was spent.</param>
/// <param name="Remaining">Budgeted minus spent; negative once over.</param>
/// <param name="PercentSpent">Spent as whole percent of the budget, rounded down; null for a budget of zero.</param>
/// <param name="State"><c>onTrack</c>; <c>warning</c>, at or above the user's warning threshold; or <c>over</c>, above the budget.</param>
/// <param name="NotConverted">How many expenses are left out of spent because no exchange rate exists on or before their date yet.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when changing the budget's amount.</param>
/// <param name="Pace">
/// In the current month only: <c>onPace</c>, or <c>aheadOfPace</c> when a larger share of the budget is spent than of the month has passed
/// (today included). Null for a past month, which shows its final result, and for one still to come.
/// </param>
public sealed record BudgetResponse(
    Guid CategoryId, BudgetCategoryResponse Category, string Budgeted, string Spent, string Remaining, int? PercentSpent, BudgetState State,
    int NotConverted, string RowVersion, BudgetPace? Pace)
{
    public static BudgetResponse From(BudgetDetails budget) =>
        new(budget.CategoryId, BudgetCategoryResponse.From(budget.Category), AmountJson.Text(budget.Budgeted), AmountJson.Text(budget.Spent),
            AmountJson.Text(budget.Remaining), budget.PercentSpent, budget.State, budget.NotConverted,
            budget.RowVersion.ToString(CultureInfo.InvariantCulture), budget.Pace);
}

/// <summary>
/// The overall budget of a month: a cap on all of its spending, against every expense of the month whether or not its category has a budget
/// of its own. Amounts are decimal strings in the user's base currency.
/// </summary>
/// <param name="Budgeted">The overall budget's amount.</param>
/// <param name="Spent">Every expense of the month, minus refunds. Derived from transactions, and negative when more came back than was spent.</param>
/// <param name="Remaining">Budgeted minus spent; negative once over.</param>
/// <param name="PercentSpent">Spent as whole percent of the budget, rounded down; null for a budget of zero.</param>
/// <param name="State"><c>onTrack</c>; <c>warning</c>, at or above the user's warning threshold; or <c>over</c>, above the budget.</param>
/// <param name="Pace">In the current month only: <c>onPace</c> or <c>aheadOfPace</c>. Null for a past month and one still to come.</param>
/// <param name="NotConverted">How many expenses are left out of spent because no exchange rate exists on or before their date yet.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when changing the overall budget's amount.</param>
public sealed record OverallBudgetResponse(
    string Budgeted, string Spent, string Remaining, int? PercentSpent, BudgetState State, BudgetPace? Pace, int NotConverted, string RowVersion)
{
    public static OverallBudgetResponse From(OverallBudgetDetails budget) =>
        new(AmountJson.Text(budget.Budgeted), AmountJson.Text(budget.Spent), AmountJson.Text(budget.Remaining), budget.PercentSpent, budget.State,
            budget.Pace, budget.NotConverted, budget.RowVersion.ToString(CultureInfo.InvariantCulture));
}

/// <summary>The category a budget is on: enough to show the budget, also when the category has since been archived.</summary>
/// <param name="ParentId">The top-level category it sits under; null for a top-level category.</param>
public sealed record BudgetCategoryResponse(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived)
{
    public static BudgetCategoryResponse From(BudgetCategoryDetails category) =>
        new(category.Id, category.ParentId, category.Name, category.Icon, category.Color, category.IsArchived);
}

/// <summary>What the budgets of a month add up to.</summary>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
public sealed record BudgetTotalsResponse(string Budgeted, string Spent, string Remaining, int NotConverted)
{
    public static BudgetTotalsResponse From(BudgetTotals totals) =>
        new(AmountJson.Text(totals.Budgeted), AmountJson.Text(totals.Spent), AmountJson.Text(totals.Remaining), totals.NotConverted);
}

/// <summary>The budgets of one budget month, unpaged: a user has at most one per category.</summary>
/// <param name="Month">The budget month, as <c>2026-11</c>.</param>
/// <param name="Currency">The user's base currency, which every amount is in.</param>
/// <param name="WarningPercent">The user's warning threshold, which the states were worked out with.</param>
/// <param name="Items">The category budgets, in the order of the category tree: by top-level category, then subcategory, each by name.</param>
/// <param name="Totals">What the category budgets add up to; the overall budget is not one of them.</param>
/// <param name="Overall">The month's overall budget; null when it has none.</param>
/// <param name="PercentOfMonthElapsed">
/// The share of the month elapsed, today included, in the user's time zone, as whole percent rounded down: what each pace compares the share
/// spent with. Null for a past month and one still to come.
/// </param>
public sealed record BudgetMonthResponse(
    string Month, Currency Currency, int WarningPercent, IReadOnlyList<BudgetResponse> Items, BudgetTotalsResponse Totals, OverallBudgetResponse? Overall,
    int? PercentOfMonthElapsed)
{
    public static BudgetMonthResponse From(BudgetMonthDetails month) =>
        new(month.Month.ToString(), month.Currency, month.WarningPercent, [.. month.Items.Select(BudgetResponse.From)], BudgetTotalsResponse.From(month.Totals),
            month.Overall is null ? null : OverallBudgetResponse.From(month.Overall), month.PercentOfMonthElapsed);
}
