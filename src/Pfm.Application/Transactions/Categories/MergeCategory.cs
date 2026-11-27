using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Transactions.Categorisation;
using Pfm.Application.Transactions.Recurring;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Categories;

/// <summary>Folds one category into another (spec CAT-3), taking all of its transactions and budgets with it.</summary>
/// <param name="Id">The category being merged, which is archived.</param>
/// <param name="TargetId">The category it is merged into.</param>
public sealed record MergeCategoryCommand(Guid Id, Guid TargetId);

public sealed class MergeCategoryValidator : AbstractValidator<MergeCategoryCommand>
{
    public MergeCategoryValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.TargetId).Required();
    }
}

/// <summary>What a merge moves, or would move.</summary>
/// <param name="Transactions">How many transactions (not deleted) have a line on the merged category; a split counts once.</param>
/// <param name="Budgets">How many budgets the merged category has, in every month together.</param>
public sealed record CategoryMergeDetails(int Transactions, int Budgets);

/// <summary>
/// Merges a category into another of the same kind: every transaction line and budget moves to the target, where
/// both have a budget in a month the amounts are added, and the merged category is archived. Its subcategories
/// move under the target, which must then be a top-level category (two levels at most), and its recurring rules
/// move to the target too, so they go on posting, as do its categorisation rules, so they go on categorising. Deleted transactions move as well, so they come back on the
/// target. Answers null when the user has no category with the id; one they have no target of is an error on
/// the target, as is every rule the merge would break, and then nothing changes.
/// </summary>
/// <remarks>
/// Everything is saved at once. A transaction, rule or budget changed by another request meanwhile makes the
/// merge start again on what was saved, and so does a budget set at the same moment on the target in a month
/// the merged category also has one.
/// </remarks>
public sealed class MergeCategoryHandler(
    IValidator<MergeCategoryCommand> validator, ICategoryRepository categories, ITransactionRepository transactions,
    IRecurringRuleRepository recurringRules, ICategorisationRuleRepository categorisationRules, IUserBudgets budgets, IBudgetNotifications budgetNotifications, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<MergeCategoryCommand, CategoryMergeDetails?>(validator)
{
    protected override Task<Result<CategoryMergeDetails?>> ExecuteAsync(MergeCategoryCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(async () =>
        {
            try
            {
                return await MergeAsync(command, cancellationToken);
            }
            catch (AlreadyExistsException meanwhile)
            {
                throw new ConcurrentChangeException(meanwhile);
            }
        });

    private async Task<Result<CategoryMergeDetails?>> MergeAsync(MergeCategoryCommand command, CancellationToken cancellationToken)
    {
        var merged = await categories.FindAsync(command.Id, cancellationToken);
        if (merged is null)
        {
            return (CategoryMergeDetails?)null;
        }

        if (await categories.FindAsync(command.TargetId, cancellationToken) is not { } target)
        {
            return Refused(command, "Choose a category to merge into.");
        }

        var now = clock.GetUtcNow();
        var subcategories = await categories.ListSubcategoriesAsync(merged.Id, cancellationToken);
        var targetSubcategories = await categories.ListSubcategoriesAsync(target.Id, cancellationToken);
        try
        {
            merged.MergeInto(target, subcategories, now);
        }
        catch (CategoryRuleViolation violation)
        {
            unitOfWork.DiscardChanges();
            return Refused(command, violation.Message);
        }

        var budgetsMerged = await budgets.MergeAsync(
            new CategoryMerge(merged.Id, target.Id, target.ParentId, [.. targetSubcategories.Where(sub => sub.Id != merged.Id).Concat(subcategories).Select(sub => sub.Id)]),
            cancellationToken);
        if (budgetsMerged is BudgetMergeOutcome.Refused refused)
        {
            unitOfWork.DiscardChanges();
            return Refused(command, refused.Reason);
        }

        var merge = (BudgetMergeOutcome.Merged)budgetsMerged;

        var moving = await transactions.ListOnCategoryAsync(merged.Id, cancellationToken);
        foreach (var transaction in moving)
        {
            transaction.MoveLines(merged, target, now);
        }

        foreach (var rule in await recurringRules.ListOnCategoryAsync(merged.Id, cancellationToken))
        {
            rule.MoveCategory(merged, target, now);
        }

        foreach (var rule in await categorisationRules.ListOnCategoryAsync(merged.Id, cancellationToken))
        {
            rule.MoveCategory(merged, target, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // What was spent in the merged category and its subcategories now counts toward the target's budgets, and its
        // budgets now meet what was spent in the target: either may move a budget into warning or over.
        await budgetNotifications.CheckAsync(
            [.. moving.Where(transaction => !transaction.IsDeleted).Select(transaction => transaction.Date).Concat(merge.BudgetedMonths).Distinct()],
            cancellationToken);
        return new CategoryMergeDetails(moving.Count(transaction => !transaction.IsDeleted), merge.Moved);
    }

    private static Result<CategoryMergeDetails?> Refused(MergeCategoryCommand command, string message) =>
        Result<CategoryMergeDetails?>.Invalid(FieldError.On(nameof(command.TargetId), message));
}

/// <summary>What merging the category would move, for the user to see before they confirm (spec CAT-3). Changes nothing.</summary>
public sealed record PreviewCategoryMerge(Guid Id);

/// <summary>Answers null when the user has no category with that id.</summary>
public sealed class PreviewCategoryMergeHandler(ICategoryRepository categories, ICategoryUsage usage, IUserBudgets budgets) : IQueryHandler
{
    public async Task<CategoryMergeDetails?> HandleAsync(PreviewCategoryMerge query, CancellationToken cancellationToken = default)
    {
        if (await categories.FindAsync(query.Id, cancellationToken) is not { } category)
        {
            return null;
        }

        return new CategoryMergeDetails(
            await usage.CountTransactionsAsync(category.Id, cancellationToken), await budgets.CountOnAsync(category.Id, cancellationToken));
    }
}
