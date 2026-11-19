using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Adds a top-level category, or a subcategory under <see cref="ParentId"/>.</summary>
public sealed record CreateCategoryCommand(Guid? ParentId, string Name, CategoryKind Kind, string Icon, string Color)
{
    public string Name { get; } = Name?.Trim() ?? "";
}

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(command => command.Name).CategoryName();
        RuleFor(command => command.Kind).CategoryKind();
        RuleFor(command => command.Icon).CategoryIcon();
        RuleFor(command => command.Color).CategoryColor();
    }
}

/// <summary>Answers the new category, or null when the parent is not one of the user's categories.</summary>
public sealed class CreateCategoryHandler(
    IValidator<CreateCategoryCommand> validator, ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateCategoryCommand, CategoryView?>(validator)
{
    protected override async Task<Result<CategoryView?>> ExecuteAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        Category? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = (await categories.ListAsync(cancellationToken)).SingleOrDefault(category => category.Id == parentId);
            if (parent is null)
            {
                return (CategoryView?)null;
            }
        }

        Category category;
        try
        {
            category = Category.Create(command.Name, command.Kind, command.Icon, command.Color, clock.GetUtcNow(), parent);
        }
        catch (CategoryRuleViolation violation)
        {
            return Result<CategoryView?>.Invalid(violation.AsErrors());
        }

        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryView.Of(category, []);
    }
}
