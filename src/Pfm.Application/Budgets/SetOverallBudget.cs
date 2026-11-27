using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;
using Pfm.Domain.Budgets;

namespace Pfm.Application.Budgets;

/// <summary>
/// Sets the overall budget of a budget month (spec BUD-5), a cap on all of its spending in the user's base
/// currency: a new one when the month has none, otherwise a new amount for the one it has, made from the row
/// version the caller last read (<paramref name="RowVersion"/>).
/// </summary>
/// <param name="Month">The budget month, as <c>2026-11</c>.</param>
/// <param name="Amount">A decimal string of zero or more, such as <c>"10000000.00"</c>.</param>
/// <param name="RowVersion">The row version of the overall budget being changed; null when setting a new one.</param>
public sealed record SetOverallBudgetCommand(string Month, string? Amount, uint? RowVersion = null)
{
    /// <summary>The month sent, read once; null when it is not a budget month.</summary>
    public BudgetMonth? ParsedMonth { get; } = BudgetRules.ReadMonth(Month);

    /// <summary>The amount sent, read once; null when it is not an amount.</summary>
    public decimal? ParsedAmount { get; } = AmountText.TryParse(Amount, out var amount) ? amount : null;
}

public sealed class SetOverallBudgetValidator : AbstractValidator<SetOverallBudgetCommand>
{
    public SetOverallBudgetValidator()
    {
        RuleFor(command => command.Month).IsBudgetMonth(command => command.ParsedMonth);
        RuleFor(command => command.Amount)
            .Must((command, _) => command.ParsedAmount is >= 0)
            .WithMessage("Enter an amount of zero or more as a decimal string such as \"10000000.00\".");
    }
}

/// <summary>What setting the overall budget came to.</summary>
public abstract record SetOverallBudgetOutcome
{
    private SetOverallBudgetOutcome()
    {
    }

    /// <summary>The month had no overall budget and has this one now.</summary>
    public sealed record Created(OverallBudgetDetails Budget) : SetOverallBudgetOutcome;

    /// <summary>The month's overall budget has its new amount.</summary>
    public sealed record Changed(OverallBudgetDetails Budget) : SetOverallBudgetOutcome;

    /// <summary>
    /// Nothing was saved: the overall budget is not as the caller last read it. It changed or was removed since
    /// the row version sent, or was set by someone else while the caller thought there was none.
    /// </summary>
    public sealed record Stale : SetOverallBudgetOutcome;

    /// <summary>Nothing was saved: the month has an overall budget, and the change did not name its row version.</summary>
    public sealed record RowVersionRequired : SetOverallBudgetOutcome;
}

/// <summary>Answers the overall budget against every expense of its month.</summary>
/// <remarks>
/// A new overall budget takes the user's base currency and locks it like any budget (spec section 3, rule 12):
/// a change of base currency saved in between has the use case made again on what was saved.
/// </remarks>
public sealed class SetOverallBudgetHandler(
    IValidator<SetOverallBudgetCommand> validator, IBudgetRepository budgets, IBaseCurrency baseCurrency, BudgetMonthReader reader, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<SetOverallBudgetCommand, SetOverallBudgetOutcome>(validator)
{
    protected override async Task<Result<SetOverallBudgetOutcome>> ExecuteAsync(SetOverallBudgetCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.RetryOnConcurrentChangeAsync(() => SetAsync(command, cancellationToken));
        }
        catch (ConcurrentChangeException)
        {
            return new SetOverallBudgetOutcome.Stale();
        }
    }

    private async Task<Result<SetOverallBudgetOutcome>> SetAsync(SetOverallBudgetCommand command, CancellationToken cancellationToken)
    {
        var (month, amount) = (command.ParsedMonth!.Value, command.ParsedAmount!.Value);
        var existing = (await budgets.ListForMonthAsync(month, cancellationToken)).Overall();
        try
        {
            if (existing is not null)
            {
                if (command.RowVersion is not { } rowVersion)
                {
                    return new SetOverallBudgetOutcome.RowVersionRequired();
                }

                if (rowVersion != existing.RowVersion)
                {
                    return new SetOverallBudgetOutcome.Stale();
                }

                existing.ChangeAmount(amount, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new SetOverallBudgetOutcome.Changed(await ReadAsync(month, cancellationToken));
            }

            if (command.RowVersion is not null)
            {
                return new SetOverallBudgetOutcome.Stale();
            }

            budgets.Add(Budget.SetOverall(month, amount, await baseCurrency.OfCurrentUserAsync(cancellationToken), clock.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new SetOverallBudgetOutcome.Created(await ReadAsync(month, cancellationToken));
        }
        catch (BudgetRuleViolation violation)
        {
            return Result<SetOverallBudgetOutcome>.Invalid(violation.AsErrors());
        }
        catch (AlreadyExistsException)
        {
            // Another request set the month's overall budget first.
            return new SetOverallBudgetOutcome.Stale();
        }
    }

    private async Task<OverallBudgetDetails> ReadAsync(BudgetMonth month, CancellationToken cancellationToken) =>
        (await reader.ReadAsync(month, cancellationToken)).Overall!;
}
