using FluentValidation;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Renames, recolours, re-icons a category or changes its kind.</summary>
public sealed record UpdateCategory(Guid Id, string Name, CategoryKind Kind, string Icon, string Color);

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategory>
{
    public UpdateCategoryValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).CategoryName();
        RuleFor(command => command.Kind).IsInEnum().WithMessage("Choose income or expense.");
        RuleFor(command => command.Icon).CategoryIcon();
        RuleFor(command => command.Color).CategoryColor();
    }
}

public sealed class UpdateCategoryHandler(ICategoryRepository categories, ICategoryUsage usage)
{
    /// <returns>The changed category, or null when it is not one of the user's categories.</returns>
    /// <exception cref="CategoryRuleViolation">The change would break a category rule.</exception>
    public async Task<CategoryView?> HandleAsync(UpdateCategory command, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(cancellationToken);
        var category = all.SingleOrDefault(candidate => candidate.Id == command.Id);
        if (category is null)
        {
            return null;
        }

        var subcategories = all.Where(candidate => candidate.ParentId == category.Id).ToList();
        if (command.Kind != category.Kind)
        {
            var hasTransactions = await usage.HasTransactionsAsync([category.Id, .. subcategories.Select(sub => sub.Id)], cancellationToken);
            category.ChangeKind(command.Kind, subcategories, hasTransactions);
        }

        category.Rename(command.Name.Trim());
        category.ChangeIcon(command.Icon);
        category.Recolour(command.Color);
        await categories.SaveChangesAsync(cancellationToken);
        return CategoryView.Of(category, subcategories);
    }
}
