namespace Pfm.Application.Transactions.Categories;

/// <summary>The user's category tree. Archived categories are left out unless asked for: pickers never show them.</summary>
public sealed record ListCategories(bool IncludeArchived = false);

public sealed class ListCategoriesHandler(ICategoryRepository categories)
{
    public async Task<IReadOnlyList<CategoryView>> HandleAsync(ListCategories query, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(cancellationToken);
        return CategoryView.Tree(query.IncludeArchived ? all : all.Where(category => !category.IsArchived));
    }
}
