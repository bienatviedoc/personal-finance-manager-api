using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

/// <summary>
/// The one place that says which categories a category stands for when transactions are filtered or added up by
/// it: itself and its subcategories (spec section 5: a category is a parent and its subcategories, two levels).
/// </summary>
internal static class CategoryAndSubcategories
{
    /// <summary>The ids of the category and of its subcategories, archived ones included, as a query.</summary>
    public static IQueryable<Guid> AndSubcategoriesOf(this IQueryable<Category> categories, Guid categoryId) =>
        categories.Where(category => category.Id == categoryId || category.ParentId == categoryId).Select(category => category.Id);
}
