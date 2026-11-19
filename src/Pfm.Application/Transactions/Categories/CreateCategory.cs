using FluentValidation;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Adds a top-level category, or a subcategory under <see cref="ParentId"/>.</summary>
public sealed record CreateCategory(Guid? ParentId, string Name, CategoryKind Kind, string Icon, string Color);

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategory>
{
    public CreateCategoryValidator()
    {
        RuleFor(command => command.Name).CategoryName();
        RuleFor(command => command.Kind).IsInEnum().WithMessage("Choose income or expense.");
        RuleFor(command => command.Icon).CategoryIcon();
        RuleFor(command => command.Color).CategoryColor();
    }
}

public sealed class CreateCategoryHandler(ICategoryRepository categories)
{
    /// <returns>The new category, or null when the parent is not one of the user's categories.</returns>
    /// <exception cref="CategoryRuleViolation">The new category would break a category rule.</exception>
    public async Task<CategoryView?> HandleAsync(CreateCategory command, CancellationToken cancellationToken)
    {
        Category? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = (await categories.ListAsync(cancellationToken)).SingleOrDefault(category => category.Id == parentId);
            if (parent is null)
            {
                return null;
            }
        }

        var category = Category.Create(command.Name.Trim(), command.Kind, command.Icon, command.Color, parent);
        categories.Add(category);
        await categories.SaveChangesAsync(cancellationToken);
        return CategoryView.Of(category, []);
    }
}
