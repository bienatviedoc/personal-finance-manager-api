using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Adds a top-level category, or a subcategory under <see cref="ParentId"/>.</summary>
public sealed record CreateCategoryCommand(Guid? ParentId, string Name, CategoryKind Kind, string Icon, string Color)
{
    public string Name { get; } = Name?.Trim() ?? "";

    /// <summary>The colour sent, read once; null when it is not a colour.</summary>
    public Domain.Color? ParsedColor { get; } = Domain.Color.TryParse(Color, out var color) ? color : null;
}

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(command => command.Name).CategoryName();
        RuleFor(command => command.Kind).CategoryKind();
        RuleFor(command => command.Icon).CategoryIcon();
        RuleFor(command => command.Color).IsColor(command => command.ParsedColor);
    }
}

/// <summary>Answers the new category, or null when the parent is not one of the user's categories.</summary>
public sealed class CreateCategoryHandler(
    IValidator<CreateCategoryCommand> validator, ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateCategoryCommand, CategoryDetails?>(validator)
{
    protected override async Task<Result<CategoryDetails?>> ExecuteAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        Category? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = await categories.FindAsync(parentId, cancellationToken);
            if (parent is null)
            {
                return (CategoryDetails?)null;
            }
        }

        Category category;
        try
        {
            category = Category.Create(command.Name, command.Kind, command.Icon, command.ParsedColor!, clock.GetUtcNow(), parent);
        }
        catch (CategoryRuleViolation violation)
        {
            return Result<CategoryDetails?>.Invalid(violation.AsErrors());
        }

        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryDetails.Of(category, []);
    }
}
