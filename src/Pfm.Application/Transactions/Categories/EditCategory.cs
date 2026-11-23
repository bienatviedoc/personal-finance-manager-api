using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
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
        RuleFor(command => command.Name).IsName();
        RuleFor(command => command.Kind).CategoryKind();
        RuleFor(command => command.Icon).CategoryIcon();
        RuleFor(command => command.Color).IsColor(command => command.ParsedColor);
    }
}

/// <summary>
/// Answers the edited category, or null when the user has no category with that id. A new kind applies to
/// the category's subcategories too, and is refused once any of them has transactions, or a budget: only an
/// expense category takes one. The edit names no row version, so when one of them takes its first transaction
/// or budget before a new kind is saved, it is made again on what was saved, and refused.
/// </summary>
public sealed class EditCategoryHandler(
    IValidator<EditCategoryCommand> validator, ICategoryRepository categories, ICategoryUsage usage, IUserBudgets budgets,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EditCategoryCommand, CategoryDetails?>(validator)
{
    protected override Task<Result<CategoryDetails?>> ExecuteAsync(EditCategoryCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => EditAsync(command, cancellationToken));

    private async Task<Result<CategoryDetails?>> EditAsync(EditCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return (CategoryDetails?)null;
        }

        var now = clock.GetUtcNow();
        var subcategories = await categories.ListSubcategoriesAsync(category.Id, cancellationToken);
        try
        {
            if (command.Kind != category.Kind)
            {
                Guid[] family = [category.Id, .. subcategories.Select(sub => sub.Id)];
                if (await budgets.AnyOnAsync(family, cancellationToken))
                {
                    return Result<CategoryDetails?>.Invalid(FieldError.On(nameof(command.Kind), "A category's kind cannot change once it has a budget."));
                }

                var hasTransactions = await usage.HasTransactionsAsync(family, cancellationToken);
                category.ChangeKind(command.Kind, subcategories, hasTransactions, now);
            }
        }
        catch (CategoryRuleViolation violation)
        {
            return Result<CategoryDetails?>.Invalid(violation.AsErrors());
        }

        category.Edit(command.Name, command.Icon, command.ParsedColor!, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryDetails.Of(category, subcategories);
    }
}
