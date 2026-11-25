namespace Pfm.Domain.Reports;

/// <summary>Who is whose subcategory, which is all a report needs to know of a category to group spending by it.</summary>
/// <param name="ParentId">The top-level category it sits under; null for a top-level category.</param>
public sealed record ReportCategory(Guid Id, Guid? ParentId);

/// <summary>What was spent in one category itself, not counting its subcategories, over a report period.</summary>
/// <param name="Spent">In the base currency, minus refunds; it can be negative.</param>
/// <param name="NotConverted">The expenses left out of <paramref name="Spent"/> because they are not converted, by transaction id.</param>
public sealed record SpentInCategory(Guid CategoryId, decimal Spent, IReadOnlyCollection<Guid> NotConverted);

/// <summary>What choosing a category in the report shows next (spec RPT-2: categories, then subcategories, then transactions).</summary>
public enum SpendingDrillDown
{
    /// <summary>Its subcategories, each with its own spending, and what was spent in the category itself.</summary>
    Subcategories = 1,

    /// <summary>The transactions that make up its spending.</summary>
    Transactions = 2,
}

/// <summary>One category's part of the spending in a report.</summary>
/// <param name="Spent">In the base currency, minus refunds, with what its subcategories spent when it is grouped with them.</param>
/// <param name="Share">Its spent as a percentage of the total, to one decimal; null when the total is not above zero.</param>
/// <param name="NotConverted">The expenses left out of <paramref name="Spent"/>, by transaction id: a split is one expense.</param>
/// <param name="PreviousSpent">What it spent in the period compared with; null when no comparison was asked for.</param>
public sealed record CategoryShare(
    Guid CategoryId, decimal Spent, decimal? Share, IReadOnlySet<Guid> NotConverted, SpendingDrillDown DrillsInto, decimal? PreviousSpent);

/// <summary>What the spending of a period comes to.</summary>
/// <param name="NotConverted">The expenses left out of <paramref name="Total"/> because they are not converted, by transaction id.</param>
public sealed record SpendingTotal(decimal Total, IReadOnlySet<Guid> NotConverted);

/// <summary>
/// Where the money went over a period (spec RPT-2): spending grouped by category, each with its share of the
/// total. At the top, every top-level category stands for itself and its subcategories; inside one top-level
/// category, each subcategory stands for itself, and the category for what was spent on it directly.
/// </summary>
/// <param name="Items">Every category that had an expense in the period, or in the period compared with; in no particular order.</param>
/// <param name="Previous">The period compared with; null when no comparison was asked for.</param>
public sealed record SpendingBreakdown(IReadOnlyList<CategoryShare> Items, SpendingTotal Total, SpendingTotal? Previous)
{
    /// <summary>Works out the breakdown from what was spent in each category itself.</summary>
    /// <param name="categories">The user's categories, archived ones included.</param>
    /// <param name="spent">What was spent in each category itself over the period.</param>
    /// <param name="previous">The same over the period compared with; null for no comparison.</param>
    /// <param name="parentId">A top-level category to break down into its subcategories; null for the top level.</param>
    /// <param name="onlyIn">
    /// The category filter: only spending in this category, and in its subcategories when it has any, counts. Null
    /// for every category.
    /// </param>
    public static SpendingBreakdown Of(
        IReadOnlyCollection<ReportCategory> categories, IReadOnlyList<SpentInCategory> spent, IReadOnlyList<SpentInCategory>? previous,
        Guid? parentId = null, Guid? onlyIn = null)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(spent);
        var parentOf = categories.ToDictionary(category => category.Id, category => category.ParentId);

        // The row a category's spending is shown in, or null when it is not shown at this level or by this filter.
        Guid? RowOf(Guid categoryId)
        {
            if (!parentOf.TryGetValue(categoryId, out var parent) || (onlyIn is { } only && categoryId != only && parent != only))
            {
                return null;
            }

            if (parentId is null)
            {
                return parent ?? categoryId;
            }

            return categoryId == parentId || parent == parentId ? categoryId : null;
        }

        var current = Grouped(spent, RowOf);
        var before = previous is null ? null : Grouped(previous, RowOf);
        var subcategoriesSpent = spent.Concat(previous ?? [])
            .Where(of => RowOf(of.CategoryId) is not null && parentOf[of.CategoryId] is not null)
            .Select(of => parentOf[of.CategoryId]!.Value)
            .ToHashSet();

        var total = TotalOf(current.Values);
        var items = current.Keys.Union(before?.Keys ?? Enumerable.Empty<Guid>())
            .Select(row =>
            {
                var (rowSpent, notConverted) = current.TryGetValue(row, out var of) ? of : (0m, []);
                var drillsInto = parentId is null && subcategoriesSpent.Contains(row) ? SpendingDrillDown.Subcategories : SpendingDrillDown.Transactions;
                return new CategoryShare(row, rowSpent, ShareOf(rowSpent, total.Total), notConverted, drillsInto, before is null ? null : before.GetValueOrDefault(row).Spent);
            })
            .ToList();

        return new SpendingBreakdown(items, total, before is null ? null : TotalOf(before.Values));
    }

    private static Dictionary<Guid, (decimal Spent, HashSet<Guid> NotConverted)> Grouped(IReadOnlyList<SpentInCategory> spent, Func<Guid, Guid?> rowOf) =>
        spent
            .Select(of => (Row: rowOf(of.CategoryId), Of: of))
            .Where(each => each.Row is not null)
            .GroupBy(each => each.Row!.Value, each => each.Of)
            .ToDictionary(row => row.Key, row => (row.Sum(of => of.Spent), row.SelectMany(of => of.NotConverted).ToHashSet()));

    private static SpendingTotal TotalOf(IEnumerable<(decimal Spent, HashSet<Guid> NotConverted)> rows)
    {
        var all = rows.ToList();
        return new SpendingTotal(all.Sum(row => row.Spent), all.SelectMany(row => row.NotConverted).ToHashSet());
    }

    /// <summary>A part of the total as a percentage to one decimal; a total of nothing, or of more refunded than spent, has no shares.</summary>
    private static decimal? ShareOf(decimal spent, decimal total) =>
        total > 0 ? decimal.Round(spent * 100 / total, 1, MidpointRounding.AwayFromZero) : null;
}
