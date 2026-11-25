using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// Where the money went (spec RPT-2): what was spent in each category over a period, with each one's share of
/// the total, in the base currency.
/// </summary>
/// <param name="ParentId">
/// A top-level category to break down into its subcategories; null for the top-level categories. A subcategory
/// breaks down into itself alone.
/// </param>
public sealed record GetSpendingByCategory(ReportFilter Filter, Guid? ParentId = null);

/// <summary>Spending by category over a period, ranked.</summary>
/// <param name="Parent">The top-level category broken down; null at the top level.</param>
/// <param name="Items">By spent, most first; then by name.</param>
/// <param name="Previous">The period compared with and what was spent in it; null when no comparison was asked for.</param>
public sealed record SpendingByCategoryDetails(
    ReportPeriod Period, Currency Currency, ReportCategoryDetails? Parent, SpendingTotal Total, IReadOnlyList<CategorySpendingDetails> Items,
    PreviousSpendingDetails? Previous);

/// <summary>One category's part of the spending.</summary>
public sealed record CategorySpendingDetails(ReportCategoryDetails Category, CategoryShare Share);

/// <summary>What was spent in the period compared with.</summary>
public sealed record PreviousSpendingDetails(ReportPeriod Period, SpendingTotal Total);

public sealed class GetSpendingByCategoryHandler(ICategoryLookup categories, ICategorySpending spending, IBaseCurrency baseCurrency) : IQueryHandler
{
    /// <returns>Null when <see cref="GetSpendingByCategory.ParentId"/> is not one of the user's categories.</returns>
    public async Task<SpendingByCategoryDetails?> HandleAsync(GetSpendingByCategory query, CancellationToken cancellationToken = default)
    {
        var filter = query.Filter;
        var all = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);
        ReportCategoryDetails? parent = null;
        if (query.ParentId is { } parentId)
        {
            if (!all.TryGetValue(parentId, out var found))
            {
                return null;
            }

            parent = ReportCategoryDetails.Of(found);
        }

        var spent = await SpentAsync(filter.Period, filter.AccountId, cancellationToken);
        var previous = filter.ComparedWith is { } comparedWith ? await SpentAsync(comparedWith, filter.AccountId, cancellationToken) : null;
        var breakdown = SpendingBreakdown.Of(
            [.. all.Values.Select(category => new ReportCategory(category.Id, category.ParentId))], spent, previous, query.ParentId, filter.CategoryId);

        var items = breakdown.Items
            .Select(share => new CategorySpendingDetails(ReportCategoryDetails.Of(all[share.CategoryId]), share))
            .OrderByDescending(item => item.Share.Spent)
            .ThenBy(item => item.Category.Name, NameOrder.Comparer)
            .ThenBy(item => item.Category.Id)
            .ToList();

        return new SpendingByCategoryDetails(
            filter.Period, await baseCurrency.OfCurrentUserAsync(cancellationToken), parent, breakdown.Total, items,
            breakdown.Previous is { } total ? new PreviousSpendingDetails(filter.ComparedWith!, total) : null);
    }

    private async Task<IReadOnlyList<SpentInCategory>> SpentAsync(ReportPeriod period, Guid? accountId, CancellationToken cancellationToken) =>
        [.. (await spending.SpentByCategoryAsync(period.From, period.To, accountId, cancellationToken))
            .Select(of => new SpentInCategory(of.CategoryId, of.Spent, of.NotConverted))];
}
