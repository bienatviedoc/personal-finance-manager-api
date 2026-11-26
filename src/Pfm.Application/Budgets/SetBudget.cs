using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>
/// Sets the budget of an expense category in a budget month, in the user's base currency: a new budget when
/// the category has none that month, otherwise a new amount for the one it has, made from the row version the
/// caller last read (<paramref name="RowVersion"/>).
/// </summary>
/// <param name="Month">The budget month, as <c>2026-11</c>.</param>
/// <param name="Amount">A decimal string of zero or more, such as <c>"3000000.00"</c>.</param>
/// <param name="RowVersion">The row version of the budget being changed; null when setting a new one.</param>
public sealed record SetBudgetCommand(string Month, Guid CategoryId, string? Amount, uint? RowVersion = null)
{
    /// <summary>The month sent, read once; null when it is not a budget month.</summary>
    public BudgetMonth? ParsedMonth { get; } = BudgetRules.ReadMonth(Month);

    /// <summary>The amount sent, read once; null when it is not an amount.</summary>
    public decimal? ParsedAmount { get; } = AmountText.TryParse(Amount, out var amount) ? amount : null;
}

public sealed class SetBudgetValidator : AbstractValidator<SetBudgetCommand>
{
    public SetBudgetValidator()
    {
        RuleFor(command => command.Month).IsBudgetMonth(command => command.ParsedMonth);
        RuleFor(command => command.CategoryId).Required();
        RuleFor(command => command.Amount)
            .Must((command, _) => command.ParsedAmount is >= 0)
            .WithMessage("Enter an amount of zero or more as a decimal string such as \"3000000.00\".");
    }
}

/// <summary>What setting a budget came to.</summary>
public abstract record SetBudgetOutcome
{
    private SetBudgetOutcome()
    {
    }

    /// <summary>The category had no budget that month and has this one now.</summary>
    public sealed record Created(BudgetDetails Budget) : SetBudgetOutcome;

    /// <summary>The category's budget that month has its new amount.</summary>
    public sealed record Changed(BudgetDetails Budget) : SetBudgetOutcome;

    /// <summary>
    /// Nothing was saved: the budget is not as the caller last read it. It changed or was removed since the
    /// row version sent, or was set by someone else while the caller thought there was none.
    /// </summary>
    public sealed record Stale : SetBudgetOutcome;

    /// <summary>Nothing was saved: the category has a budget that month, and the change did not name its row version.</summary>
    public sealed record RowVersionRequired : SetBudgetOutcome;
}

/// <summary>
/// Answers the budget against what was spent, or null when the user has no category with that id.
/// </summary>
/// <remarks>
/// A new budget takes the user's base currency, which it locks from then on (spec section 3, rule 12). Two
/// things can get in between reading and saving, and both have the use case made again on what was saved: a
/// change of the base currency, and a change of the category's kind. A budget set at the same moment on the
/// category's parent or subcategory cannot be seen before saving, so the month is read again afterwards, and
/// a budget that turns out to break rule 6 is taken back.
/// </remarks>
public sealed class SetBudgetHandler(
    IValidator<SetBudgetCommand> validator, IBudgetRepository budgets, ICategoryLookup categories, IBaseCurrency baseCurrency, BudgetMonthReader reader,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<SetBudgetCommand, SetBudgetOutcome?>(validator)
{
    private static readonly SetBudgetOutcome? NoCategory = null;

    protected override async Task<Result<SetBudgetOutcome?>> ExecuteAsync(SetBudgetCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.RetryOnConcurrentChangeAsync(() => SetAsync(command, cancellationToken));
        }
        catch (ConcurrentChangeException)
        {
            return new SetBudgetOutcome.Stale();
        }
    }

    private async Task<Result<SetBudgetOutcome?>> SetAsync(SetBudgetCommand command, CancellationToken cancellationToken)
    {
        var (month, amount) = (command.ParsedMonth!.Value, command.ParsedAmount!.Value);
        var tree = new CategoryTree(await categories.ListAsync(cancellationToken));
        if (tree.BudgetCategory(command.CategoryId) is not { } category)
        {
            return NoCategory;
        }

        var ofMonth = await budgets.ListForMonthAsync(month, cancellationToken);
        var existing = ofMonth.SingleOrDefault(budget => budget.CategoryId == category.Id);
        try
        {
            if (existing is not null)
            {
                if (command.RowVersion is not { } rowVersion)
                {
                    return new SetBudgetOutcome.RowVersionRequired();
                }

                if (rowVersion != existing.RowVersion)
                {
                    return new SetBudgetOutcome.Stale();
                }

                existing.ChangeAmount(amount, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new SetBudgetOutcome.Changed(await ReadAsync(month, category.Id, cancellationToken));
            }

            if (command.RowVersion is not null)
            {
                return new SetBudgetOutcome.Stale();
            }

            var budget = Budget.Set(
                category, month, amount, await baseCurrency.OfCurrentUserAsync(cancellationToken), ofMonth.CategoryIds(),
                clock.GetUtcNow());
            budgets.Add(budget);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (await SetMeanwhileOnParentOrSubcategoryAsync(category, month, cancellationToken) is { } violation)
            {
                budgets.Remove(budget);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<SetBudgetOutcome?>.Invalid(violation.AsErrors());
            }

            return new SetBudgetOutcome.Created(await ReadAsync(month, category.Id, cancellationToken));
        }
        catch (BudgetRuleViolation violation)
        {
            return Result<SetBudgetOutcome?>.Invalid(violation.AsErrors());
        }
        catch (AlreadyExistsException)
        {
            // Another request set the category's budget for the month first.
            return new SetBudgetOutcome.Stale();
        }
    }

    /// <summary>The rule a budget just saved breaks, when another request budgeted the category's parent or subcategory in between.</summary>
    private async Task<BudgetRuleViolation?> SetMeanwhileOnParentOrSubcategoryAsync(BudgetCategory category, BudgetMonth month, CancellationToken cancellationToken)
    {
        List<Guid> others = [.. (await budgets.ListForMonthAsync(month, cancellationToken)).CategoryIds().Where(id => id != category.Id)];
        try
        {
            Budget.EnsureCanBeSetOn(category, others);
            return null;
        }
        catch (BudgetRuleViolation violation)
        {
            return violation;
        }
    }

    private async Task<BudgetDetails> ReadAsync(BudgetMonth month, Guid categoryId, CancellationToken cancellationToken) =>
        (await reader.ReadAsync(month, cancellationToken)).Items.Single(budget => budget.CategoryId == categoryId);
}
