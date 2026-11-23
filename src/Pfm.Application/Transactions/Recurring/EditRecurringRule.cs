using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Replaces what a recurring rule's next transactions say and when they are due. Transactions it already
/// posted stay as they are, and a new schedule carries on after the last occurrence posted or skipped.
/// </summary>
public sealed record EditRecurringRuleCommand(
    Guid Id, string? Type, Guid AccountId, string? Amount, Guid CategoryId, string? Payee, string? Note, string? Frequency, int? Interval,
    string? StartDate, string? EndDate)
    : IRecurringRuleCommand
{
    public RecurringRuleFields Parsed { get; } = RecurringRuleFields.Read(Type, Amount, Frequency, StartDate, EndDate);
}

public sealed class EditRecurringRuleValidator : AbstractValidator<EditRecurringRuleCommand>
{
    public EditRecurringRuleValidator()
    {
        RuleFor(command => command.Id).Required();
        this.RecurringRuleFieldRules();
    }
}

/// <summary>Answers the edited rule, or null when the user has no rule with that id.</summary>
public sealed class EditRecurringRuleHandler(
    IValidator<EditRecurringRuleCommand> validator, IRecurringRuleRepository rules, RecurringRuleContext context, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<EditRecurringRuleCommand, RecurringRuleDetails?>(validator)
{
    /// <summary>An edit names no row version, so one that meets the worker posting the rule at the same moment is made again on what was saved.</summary>
    protected override Task<Result<RecurringRuleDetails?>> ExecuteAsync(EditRecurringRuleCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => EditAsync(command, cancellationToken));

    private async Task<Result<RecurringRuleDetails?>> EditAsync(EditRecurringRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await rules.FindAsync(command.Id, cancellationToken);
        if (rule is null)
        {
            return (RecurringRuleDetails?)null;
        }

        var read = await context.ReadAsync(command, rule, cancellationToken);
        if (!read.IsValid)
        {
            return Result<RecurringRuleDetails?>.Invalid(read.Errors);
        }

        var (account, category) = read.Outcome;
        try
        {
            rule.Edit(
                command.Parsed.Type!.Value, account.Id, account.Currency, command.Parsed.Amount!.Value, category, command.Payee, command.Note,
                command.Schedule(), clock.GetUtcNow());
        }
        catch (RecurringRuleViolation violation)
        {
            return Result<RecurringRuleDetails?>.Invalid(violation.AsErrors());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RecurringRuleDetails.Of(rule, account.Currency);
    }
}
