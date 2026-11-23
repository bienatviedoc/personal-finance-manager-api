using Microsoft.AspNetCore.Http.HttpResults;
using Pfm.Application.Reports;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Api.Reports;

/// <summary>Reports over HTTP (spec section 7). The use cases work everything out; these only map requests and answers.</summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        var reports = api.MapGroup("/reports").WithTags("Reports").ProducesProblem(StatusCodes.Status401Unauthorized);

        reports.MapGet("/spending-by-category", SpendingByCategory).WithName("GetSpendingByCategory")
            .WithDescription("Where the money went over a period: what the expenses in each category came to, minus refunds, with each one's share of the total, in the user's base currency and ranked, most first. Each expense counts as converted at the exchange rate of its own date; one that is not converted is left out and counted in notConverted. Transfers and income never count, and each line of a split counts in its own category. Without parentId each top-level category stands for itself and its subcategories; with it, each of that category's subcategories stands for itself, and the category for what was spent on it directly.");

        return api;
    }

    /// <param name="from">The first day of the period, <c>YYYY-MM-DD</c>.</param>
    /// <param name="to">The last day of the period, <c>YYYY-MM-DD</c>; on or after <paramref name="from"/>.</param>
    /// <param name="accountId">One of the user's accounts: only its expenses count.</param>
    /// <param name="categoryId">One of the user's categories: only expenses in it, and in its subcategories when it has any, count.</param>
    /// <param name="parentId">A top-level category to break down into its subcategories; left out for the top-level categories.</param>
    /// <param name="compare">Also work out what each category spent in the period before: as many whole months before a period of whole calendar months, otherwise as many days.</param>
    private static async Task<Results<Ok<SpendingByCategoryResponse>, NotFound, ValidationProblem>> SpendingByCategory(
        GetSpendingByCategoryHandler handler, CancellationToken cancellationToken,
        string? from = null, string? to = null, string? accountId = null, string? categoryId = null, string? parentId = null, bool compare = false)
    {
        var asked = new ReportParameters();
        var filter = asked.Filter(from, to, accountId, categoryId, compare);
        var parent = asked.Id(nameof(parentId), parentId);
        if (filter is null || asked.Refused.Count > 0)
        {
            return TypedResults.ValidationProblem(asked.Refused);
        }

        var report = await handler.HandleAsync(new GetSpendingByCategory(filter, parent), cancellationToken);
        return report is null ? TypedResults.NotFound() : TypedResults.Ok(SpendingByCategoryResponse.Of(report));
    }
}

/// <summary>A category as a report shows it: enough to name and colour it, also when it has since been archived.</summary>
/// <param name="ParentId">The top-level category it sits under; null for a top-level category.</param>
/// <param name="Color">The colour it is shown in, as <c>#rrggbb</c>.</param>
public sealed record ReportCategoryResponse(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived)
{
    public static ReportCategoryResponse From(ReportCategoryDetails category) =>
        new(category.Id, category.ParentId, category.Name, category.Icon, category.Color, category.IsArchived);
}

/// <summary>One category's part of the spending. Amounts are decimal strings in the user's base currency.</summary>
/// <param name="Spent">What its expenses came to, minus refunds, with its subcategories' at the top level; negative when more came back than was spent.</param>
/// <param name="Share">Spent as a percentage of the total, to one decimal; null when the total is not above zero.</param>
/// <param name="NotConverted">How many of its expenses are left out of spent because no exchange rate exists on or before their date yet.</param>
/// <param name="DrillsInto"><c>subcategories</c> when its subcategories spent something in either period, which <c>parentId</c> shows; otherwise <c>transactions</c>, which <c>/reports/spending-by-category/transactions</c> lists.</param>
/// <param name="PreviousSpent">What it spent in the period before; null unless <c>compare</c> was asked for.</param>
public sealed record SpendingByCategoryItemResponse(
    ReportCategoryResponse Category, string Spent, decimal? Share, int NotConverted, SpendingDrillDown DrillsInto, string? PreviousSpent)
{
    public static SpendingByCategoryItemResponse From(CategorySpendingDetails item) =>
        new(ReportCategoryResponse.From(item.Category), AmountJson.Text(item.Share.Spent), item.Share.Share, item.Share.NotConverted.Count, item.Share.DrillsInto,
            item.Share.PreviousSpent is { } previous ? AmountJson.Text(previous) : null);
}

/// <summary>The period compared with and what was spent in it.</summary>
/// <param name="NotConverted">How many expenses of that period are left out of its total because they are not converted.</param>
public sealed record PreviousSpendingResponse(DateOnly From, DateOnly To, string Total, int NotConverted)
{
    public static PreviousSpendingResponse Of(PreviousSpendingDetails previous) =>
        new(previous.Period.From, previous.Period.To, AmountJson.Text(previous.Total.Total), previous.Total.NotConverted.Count);
}

/// <summary>Where the money went over a period, unpaged: one item per category that had an expense.</summary>
/// <param name="Currency">The user's base currency, which every amount is in.</param>
/// <param name="Parent">The top-level category broken down; null at the top level.</param>
/// <param name="Total">What every item's spent adds up to.</param>
/// <param name="NotConverted">How many expenses are left out of the total because they are not converted; a split is one.</param>
/// <param name="Items">By spent, most first, then by name. With <c>compare</c>, a category that spent only in the period before is among them, at zero.</param>
/// <param name="Previous">The period before; null unless <c>compare</c> was asked for.</param>
public sealed record SpendingByCategoryResponse(
    DateOnly From, DateOnly To, Currency Currency, ReportCategoryResponse? Parent, string Total, int NotConverted,
    IReadOnlyList<SpendingByCategoryItemResponse> Items, PreviousSpendingResponse? Previous)
{
    public static SpendingByCategoryResponse Of(SpendingByCategoryDetails report) =>
        new(report.Period.From, report.Period.To, report.Currency, report.Parent is { } parent ? ReportCategoryResponse.From(parent) : null,
            AmountJson.Text(report.Total.Total), report.Total.NotConverted.Count, [.. report.Items.Select(SpendingByCategoryItemResponse.From)],
            report.Previous is { } previous ? PreviousSpendingResponse.Of(previous) : null);
}
