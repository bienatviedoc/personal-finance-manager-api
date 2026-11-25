using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>
/// Works out a budget month: each budget against what was spent in its category that month. Spent is asked
/// of Transactions every time, by its own definition of it (expenses minus refunds, a split by its lines),
/// and never stored, so a late edit or import corrects the budget by itself (spec section 6).
/// </summary>
public sealed class BudgetMonthReader(
    IBudgetRepository budgets, ICategoryLookup categories, ICategorySpending spending, IBaseCurrency baseCurrency, IBudgetWarningThreshold warningThreshold)
{
    public async Task<BudgetMonthDetails> ReadAsync(BudgetMonth month, CancellationToken cancellationToken)
    {
        var ofMonth = await budgets.ListForMonthAsync(month, cancellationToken);
        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        var warningPercent = await warningThreshold.OfCurrentUserAsync(cancellationToken);
        var spent = ofMonth.Count == 0
            ? []
            : (await spending.SpentByCategoryAsync(month.FirstDay, month.LastDay, accountId: null, cancellationToken))
                .Select(of => new CategorySpending(of.CategoryId, of.Spent, of.NotConverted))
                .ToList();

        var progress = ofMonth
            .Select(budget => (Category: tree.Find(budget.CategoryId)!, Progress: BudgetProgress.Of(budget, tree.BudgetCategory(budget.CategoryId)!, spent, warningPercent)))
            .OrderBy(row => tree.TopLevelName(row.Category), NameOrder.Comparer)
            .ThenBy(row => row.Category.ParentId ?? row.Category.Id)
            .ThenBy(row => row.Category.ParentId is not null)
            .ThenBy(row => row.Category.Name, NameOrder.Comparer)
            .ThenBy(row => row.Category.Id)
            .ToList();

        return new BudgetMonthDetails(
            month,
            await baseCurrency.OfCurrentUserAsync(cancellationToken),
            warningPercent,
            [.. progress.Select(row => Details(row.Category, row.Progress))],
            BudgetTotals.Of([.. progress.Select(row => row.Progress)]));
    }

    private static BudgetDetails Details(CategorySummary category, BudgetProgress progress) =>
        new(category.Id,
            new BudgetCategoryDetails(category.Id, category.ParentId, category.Name, category.Icon, category.Color, category.IsArchived),
            progress.Budgeted, progress.Spent, progress.Remaining, progress.PercentSpent, progress.State, progress.NotConverted, progress.Budget.RowVersion);
}

/// <summary>The user's categories as Budgets reads them: who is whose subcategory.</summary>
internal sealed class CategoryTree(IReadOnlyList<CategorySummary> categories)
{
    private readonly Dictionary<Guid, CategorySummary> _byId = categories.ToDictionary(category => category.Id);

    private readonly ILookup<Guid?, Guid> _subcategories = categories.ToLookup(category => category.ParentId, category => category.Id);

    public CategorySummary? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The category as the domain's budget rules take it; null when the user has no such category.</summary>
    public BudgetCategory? BudgetCategory(Guid id) =>
        Find(id) is { } category ? new BudgetCategory(category.Id, category.ParentId, [.. _subcategories[category.Id]], category.IsExpense, category.IsArchived) : null;

    /// <summary>Every category as the domain's budget rules take it.</summary>
    public IReadOnlyList<BudgetCategory> BudgetCategories() => [.. _byId.Keys.Select(id => BudgetCategory(id)!)];

    /// <summary>The name a category is ordered under: its parent's when it has one, else its own.</summary>
    public string TopLevelName(CategorySummary category) =>
        category.ParentId is { } parentId && Find(parentId) is { } parent ? parent.Name : category.Name;
}
