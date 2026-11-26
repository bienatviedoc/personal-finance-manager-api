using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>Removes the overall budget of a budget month, leaving its category budgets as they are.</summary>
/// <param name="Month">The budget month, as <c>2026-11</c>.</param>
public sealed record RemoveOverallBudgetCommand(string Month)
{
    /// <summary>The month sent, read once; null when it is not a budget month.</summary>
    public BudgetMonth? ParsedMonth { get; } = BudgetRules.ReadMonth(Month);
}

public sealed class RemoveOverallBudgetValidator : AbstractValidator<RemoveOverallBudgetCommand>
{
    public RemoveOverallBudgetValidator() => RuleFor(command => command.Month).IsBudgetMonth(command => command.ParsedMonth);
}

/// <summary>Answers null when the month has no overall budget.</summary>
public sealed class RemoveOverallBudgetHandler(IValidator<RemoveOverallBudgetCommand> validator, IBudgetRepository budgets, IUnitOfWork unitOfWork)
    : CommandHandler<RemoveOverallBudgetCommand, Done?>(validator)
{
    /// <summary>Removing names no row version, so an overall budget whose amount is changed at the same moment is removed all the same.</summary>
    protected override Task<Result<Done?>> ExecuteAsync(RemoveOverallBudgetCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<Done?>>(async () =>
        {
            var ofMonth = await budgets.ListForMonthAsync(command.ParsedMonth!.Value, cancellationToken);
            if (ofMonth.SingleOrDefault(budget => budget.IsOverall) is not { } budget)
            {
                return (Done?)null;
            }

            budgets.Remove(budget);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new Done();
        });
}
