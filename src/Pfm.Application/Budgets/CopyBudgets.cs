using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Copies the budgets of one budget month into another in one action (spec BUD-4), such as last month's into this one.</summary>
/// <param name="Month">The budget month copied into, as <c>2026-11</c>.</param>
/// <param name="SourceMonth">The budget month copied from, as <c>2026-10</c>.</param>
public sealed record CopyBudgetsCommand(string Month, string SourceMonth)
{
    /// <summary>The month sent, read once; null when it is not a budget month.</summary>
    public BudgetMonth? ParsedMonth { get; } = BudgetRules.ReadMonth(Month);

    /// <summary>The source month sent, read once; null when it is not a budget month.</summary>
    public BudgetMonth? ParsedSourceMonth { get; } = BudgetRules.ReadMonth(SourceMonth);
}

public sealed class CopyBudgetsValidator : AbstractValidator<CopyBudgetsCommand>
{
    public CopyBudgetsValidator()
    {
        RuleFor(command => command.Month).IsBudgetMonth(command => command.ParsedMonth);
        RuleFor(command => command.SourceMonth).IsBudgetMonth(command => command.ParsedSourceMonth)
            .Must((command, _) => command.ParsedSourceMonth != command.ParsedMonth).WithMessage("Copy from another month.");
    }
}

/// <summary>What copying budgets came to.</summary>
/// <param name="Copied">How many budgets the month gained.</param>
public sealed record CopyBudgetsOutcome(int Copied);

/// <summary>
/// Copies each budget of the source month whose category takes a new budget in the month: one that has none
/// there yet, is not archived, and whose parent or subcategory has none there either (spec section 3, rule 6).
/// The rest are left as they are, and the outcome counts only what was copied.
/// </summary>
/// <remarks>
/// A budget set in the month by another request while copying makes the copy start again on what was saved,
/// so that budget is kept as it was set. One set at the same moment on a copied category's parent or
/// subcategory cannot be seen before saving, so the month is read again afterwards, and a copied budget that
/// turns out to break rule 6 is taken back and not counted.
/// </remarks>
public sealed class CopyBudgetsHandler(
    IValidator<CopyBudgetsCommand> validator, IBudgetRepository budgets, ICategoryLookup categories, IBaseCurrency baseCurrency, IBudgetNotifications notifications,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CopyBudgetsCommand, CopyBudgetsOutcome>(validator)
{
    protected override Task<Result<CopyBudgetsOutcome>> ExecuteAsync(CopyBudgetsCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(async () =>
        {
            try
            {
                return await CopyAsync(command, cancellationToken);
            }
            catch (AlreadyExistsException meanwhile)
            {
                throw new ConcurrentChangeException(meanwhile);
            }
        });

    private async Task<Result<CopyBudgetsOutcome>> CopyAsync(CopyBudgetsCommand command, CancellationToken cancellationToken)
    {
        var (month, sourceMonth) = (command.ParsedMonth!.Value, command.ParsedSourceMonth!.Value);
        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        HashSet<Guid> budgeted = [.. (await budgets.ListForMonthAsync(month, cancellationToken)).Select(budget => budget.CategoryId)];
        List<(Budget Budget, BudgetCategory Category)> copied = [];
        foreach (var source in await budgets.ListForMonthAsync(sourceMonth, cancellationToken))
        {
            if (budgeted.Contains(source.CategoryId) || tree.BudgetCategory(source.CategoryId) is not { } category)
            {
                continue;
            }

            try
            {
                var budget = Budget.Set(category, month, source.Amount, currency, budgeted, clock.GetUtcNow());
                budgets.Add(budget);
                copied.Add((budget, category));
            }
            catch (BudgetRuleViolation)
            {
                // The category takes no new budget this month: it was archived, or its parent or a subcategory has one.
                continue;
            }

            budgeted.Add(source.CategoryId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var takenBack = await TakeBackWhatBreaksRuleSixAsync(copied, month, cancellationToken);
        if (copied.Count > takenBack)
        {
            await notifications.CheckAsync([month.FirstDay], cancellationToken);
        }

        return new CopyBudgetsOutcome(copied.Count - takenBack);
    }

    /// <summary>
    /// Takes back each budget just copied whose category's parent or subcategory another request budgeted in the
    /// month in between: neither could see the other before saving (rule 6, as <c>SetBudget</c> does).
    /// </summary>
    /// <returns>How many were taken back.</returns>
    private async Task<int> TakeBackWhatBreaksRuleSixAsync(
        IReadOnlyList<(Budget Budget, BudgetCategory Category)> copied, BudgetMonth month, CancellationToken cancellationToken)
    {
        if (copied.Count == 0)
        {
            return 0;
        }

        List<Guid> saved = [.. (await budgets.ListForMonthAsync(month, cancellationToken)).Select(budget => budget.CategoryId)];
        var takenBack = 0;
        foreach (var (budget, category) in copied)
        {
            try
            {
                Budget.EnsureCanBeSetOn(category, [.. saved.Where(id => id != category.Id)]);
            }
            catch (BudgetRuleViolation)
            {
                budgets.Remove(budget);
                saved.Remove(category.Id);
                takenBack++;
            }
        }

        if (takenBack > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return takenBack;
    }
}
