using FluentValidation;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Archives a category and its subcategories: gone from pickers, still on the transactions that use them.</summary>
public sealed record ArchiveCategory(Guid Id);

public sealed class ArchiveCategoryValidator : AbstractValidator<ArchiveCategory>
{
    public ArchiveCategoryValidator() => RuleFor(command => command.Id).NotEmpty();
}

public sealed class ArchiveCategoryHandler(ICategoryRepository categories)
{
    /// <returns>False when the category is not one of the user's categories.</returns>
    public async Task<bool> HandleAsync(ArchiveCategory command, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(cancellationToken);
        var category = all.SingleOrDefault(candidate => candidate.Id == command.Id);
        if (category is null)
        {
            return false;
        }

        category.Archive([.. all.Where(candidate => candidate.ParentId == category.Id)]);
        await categories.SaveChangesAsync(cancellationToken);
        return true;
    }
}
