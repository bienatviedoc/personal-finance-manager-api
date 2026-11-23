using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Removes the budget a category has in a budget month.</summary>
/// <param name="Month">The budget month, as <c>2026-11</c>.</param>
public sealed record RemoveBudgetCommand(string Month, Guid CategoryId)
{
    /// <summary>The month sent, read once; null when it is not a budget month.</summary>
    public BudgetMonth? ParsedMonth { get; } = BudgetRules.ReadMonth(Month);
}

public sealed class RemoveBudgetValidator : AbstractValidator<RemoveBudgetCommand>
{
    public RemoveBudgetValidator()
    {
        RuleFor(command => command.Month).IsBudgetMonth(command => command.ParsedMonth);
        RuleFor(command => command.CategoryId).Required();
    }
}

/// <summary>Answers null when the category has no budget that month, or is not one of the user's.</summary>
public sealed class RemoveBudgetHandler(IValidator<RemoveBudgetCommand> validator, IBudgetRepository budgets, IUnitOfWork unitOfWork)
    : CommandHandler<RemoveBudgetCommand, Done?>(validator)
{
    /// <summary>Removing names no row version, so a budget whose amount is changed at the same moment is removed all the same.</summary>
    protected override Task<Result<Done?>> ExecuteAsync(RemoveBudgetCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<Done?>>(async () =>
        {
            var ofMonth = await budgets.ListForMonthAsync(command.ParsedMonth!.Value, cancellationToken);
            if (ofMonth.SingleOrDefault(budget => budget.CategoryId == command.CategoryId) is not { } budget)
            {
                return (Done?)null;
            }

            budgets.Remove(budget);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new Done();
        });
}
