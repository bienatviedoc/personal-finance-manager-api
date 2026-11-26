using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Replaces what a recurring rule's next transactions say and when they are due, from the row version the
/// caller last read (<paramref name="RowVersion"/>). Transactions it already posted stay as they are, and a new
/// schedule carries on after the last occurrence posted or skipped.
/// </summary>
public sealed record EditRecurringRuleCommand(
    Guid Id, uint RowVersion, string? Type, Guid AccountId, string? Amount, Guid CategoryId, string? Payee, string? Note, string? Frequency, int? Interval,
    string? StartDate, string? EndDate, string? Mode = null)
    : IRecurringRuleCommand
{
    public RecurringRuleFields Parsed { get; } = RecurringRuleFields.Read(Type, Amount, Frequency, StartDate, EndDate, Mode);
}

public sealed class EditRecurringRuleValidator : AbstractValidator<EditRecurringRuleCommand>
{
    public EditRecurringRuleValidator()
    {
        RuleFor(command => command.Id).Required();
        this.RecurringRuleFieldRules();
    }
}

/// <summary>What editing a recurring rule came to: the edited rule, or nothing saved because it had changed.</summary>
/// <param name="Edited">Null when the edit was made from a stale row version.</param>
public sealed record EditRecurringRuleOutcome(RecurringRuleDetails? Edited)
{
    /// <summary>The rule changed since the row version the edit was made from; nothing was saved.</summary>
    public static readonly EditRecurringRuleOutcome Stale = new((RecurringRuleDetails?)null);
}

/// <summary>Answers the edited rule; null when the user has no rule with that id.</summary>
public sealed class EditRecurringRuleHandler(
    IValidator<EditRecurringRuleCommand> validator, IRecurringRuleRepository rules, RecurringRuleContext context, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<EditRecurringRuleCommand, EditRecurringRuleOutcome?>(validator)
{
    /// <summary>
    /// Made from a row version: a rule that changed meanwhile, by another edit, a pause or the worker posting an
    /// occurrence, is stale. Made again when something else it was read with changed before it was saved; when it
    /// was the rule that changed, the row version is no longer its own and the edit is stale.
    /// </summary>
    protected override async Task<Result<EditRecurringRuleOutcome?>> ExecuteAsync(EditRecurringRuleCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.RetryOnConcurrentChangeAsync(() => EditAsync(command, cancellationToken));
        }
        catch (ConcurrentChangeException)
        {
            return EditRecurringRuleOutcome.Stale;
        }
    }

    private async Task<Result<EditRecurringRuleOutcome?>> EditAsync(EditRecurringRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await rules.FindAsync(command.Id, cancellationToken);
        if (rule is null)
        {
            return (EditRecurringRuleOutcome?)null;
        }

        if (rule.RowVersion != command.RowVersion)
        {
            return EditRecurringRuleOutcome.Stale;
        }

        var read = await context.ReadAsync(command, rule, cancellationToken);
        if (!read.IsValid)
        {
            return Result<EditRecurringRuleOutcome?>.Invalid(read.Errors);
        }

        var (account, category) = read.Outcome;
        try
        {
            rule.Edit(
                command.Parsed.Type!.Value, account.Id, account.Currency, command.Parsed.Amount!.Value, category, command.Payee, command.Note,
                command.Schedule(), clock.GetUtcNow(), command.Parsed.Mode!.Value);
        }
        catch (RecurringRuleViolation violation)
        {
            return Result<EditRecurringRuleOutcome?>.Invalid(violation.AsErrors());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EditRecurringRuleOutcome(RecurringRuleDetails.Of(rule, account.Currency));
    }
}
