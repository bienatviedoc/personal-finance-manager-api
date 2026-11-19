using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Renames, recolours or re-icons a category, or changes its kind.</summary>
public sealed record EditCategoryCommand(Guid Id, string Name, CategoryKind Kind, string Icon, string Color)
{
    public string Name { get; } = Name?.Trim() ?? "";

    /// <summary>The colour sent, read once; null when it is not a colour.</summary>
    public Domain.Color? ParsedColor { get; } = Domain.Color.TryParse(Color, out var color) ? color : null;
}

public sealed class EditCategoryValidator : AbstractValidator<EditCategoryCommand>
{
    public EditCategoryValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.Name).CategoryName();
        RuleFor(command => command.Kind).CategoryKind();
        RuleFor(command => command.Icon).CategoryIcon();
        RuleFor(command => command.Color).IsColor(command => command.ParsedColor);
    }
}

/// <summary>
/// Answers the edited category, or null when the user has no category with that id. A new kind applies to
/// the category's subcategories too, and is refused once any of them has transactions.
/// </summary>
public sealed class EditCategoryHandler(
    IValidator<EditCategoryCommand> validator, ICategoryRepository categories, ICategoryUsage usage, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EditCategoryCommand, CategoryView?>(validator)
{
    protected override async Task<Result<CategoryView?>> ExecuteAsync(EditCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return (CategoryView?)null;
        }

        var now = clock.GetUtcNow();
        var subcategories = await categories.ListSubcategoriesAsync(category.Id, cancellationToken);
        try
        {
            if (command.Kind != category.Kind)
            {
                var hasTransactions = await usage.HasTransactionsAsync([category.Id, .. subcategories.Select(sub => sub.Id)], cancellationToken);
                category.ChangeKind(command.Kind, subcategories, hasTransactions, now);
            }
        }
        catch (CategoryRuleViolation violation)
        {
            return Result<CategoryView?>.Invalid(violation.AsErrors());
        }

        category.Edit(command.Name, command.Icon, command.ParsedColor!, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryView.Of(category, subcategories);
    }
}
