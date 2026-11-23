using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Answers other modules' questions about categories from the user's own.</summary>
public sealed class CategoryLookup(ICategoryRepository categories) : ICategoryLookup
{
    public async Task<IReadOnlyList<CategorySummary>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await categories.ListAsync(cancellationToken)).Select(category => new CategorySummary(
            category.Id, category.ParentId, category.Name, category.Kind == CategoryKind.Expense, category.Icon, category.Color.Hex, category.IsArchived))];
}
