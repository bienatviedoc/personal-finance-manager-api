using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>A category as shown to its user, with its subcategories when it is a top-level one.</summary>
public sealed record CategoryView(
    Guid Id,
    Guid? ParentId,
    string Name,
    CategoryKind Kind,
    string Icon,
    string Color,
    bool IsArchived,
    IReadOnlyList<CategoryView> Subcategories)
{
    /// <summary>The tree of the given categories: top-level ones, each with its subcategories, ordered by name.</summary>
    public static IReadOnlyList<CategoryView> Tree(IEnumerable<Category> categories)
    {
        var all = categories.OrderByName(category => category.Name, category => category.Id).ToList();
        return [.. all.Where(category => category.ParentId is null).Select(parent => Of(parent, all.Where(sub => sub.ParentId == parent.Id)))];
    }

    public static CategoryView Of(Category category, IEnumerable<Category> subcategories) =>
        new(category.Id, category.ParentId, category.Name, category.Kind, category.Icon, category.Color.Hex, category.IsArchived,
            [.. subcategories.Select(sub => Of(sub, []))]);
}
