using FluentValidation;
using Pfm.Application.Abstractions;
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
        RuleFor(command => command.SourceMonth).IsBudgetMonth(command => command.ParsedSourceMonth);
    }
}

/// <summary>What copying budgets came to.</summary>
/// <param name="Copied">How many budgets the month gained.</param>
public sealed record CopyBudgetsOutcome(int Copied);

public sealed class CopyBudgetsHandler(
    IValidator<CopyBudgetsCommand> validator, IBudgetRepository budgets, ICategoryLookup categories, IBaseCurrency baseCurrency, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<CopyBudgetsCommand, CopyBudgetsOutcome>(validator)
{
    protected override async Task<Result<CopyBudgetsOutcome>> ExecuteAsync(CopyBudgetsCommand command, CancellationToken cancellationToken)
    {
        var (month, sourceMonth) = (command.ParsedMonth!.Value, command.ParsedSourceMonth!.Value);
        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        var currency = await baseCurrency.OfCurrentUserAsync(cancellationToken);
        HashSet<Guid> budgeted = [.. (await budgets.ListForMonthAsync(month, cancellationToken)).Select(budget => budget.CategoryId)];
        var copied = 0;
        foreach (var source in await budgets.ListForMonthAsync(sourceMonth, cancellationToken))
        {
            if (budgeted.Contains(source.CategoryId) || tree.BudgetCategory(source.CategoryId) is not { } category)
            {
                continue;
            }

            try
            {
                budgets.Add(Budget.Set(category, month, source.Amount, currency, budgeted, clock.GetUtcNow()));
            }
            catch (BudgetRuleViolation)
            {
                // The category takes no new budget this month: it was archived, or its parent or a subcategory has one.
                continue;
            }

            budgeted.Add(source.CategoryId);
            copied++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CopyBudgetsOutcome(copied);
    }
}
