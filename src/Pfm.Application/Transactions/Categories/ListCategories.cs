using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categories;

/// <summary>The user's category tree. Archived categories are left out unless asked for: pickers never show them.</summary>
public sealed record ListCategories(bool IncludeArchived = false);

public sealed class ListCategoriesHandler(ICategoryRepository categories) : IQueryHandler
{
    public async Task<IReadOnlyList<CategoryDetails>> HandleAsync(ListCategories query, CancellationToken cancellationToken = default)
    {
        var all = await categories.ListAsync(cancellationToken);
        return CategoryDetails.Tree(query.IncludeArchived ? all : all.Where(category => !category.IsArchived));
    }
}
