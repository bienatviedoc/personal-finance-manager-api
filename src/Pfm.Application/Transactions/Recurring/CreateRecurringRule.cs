using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Sets up a recurring rule: income or an expense that the worker posts on each due date (spec REC-1). A
/// start date in the past is allowed: the occurrences since then are posted, each on its own due date.
/// </summary>
/// <param name="Type"><c>income</c> or <c>expense</c>; a rule is never a transfer.</param>
/// <param name="Amount">A decimal string greater than zero, in the currency of the account.</param>
/// <param name="Frequency"><c>weekly</c>, <c>monthly</c>, <c>yearly</c> or <c>everyNDays</c>.</param>
/// <param name="Interval">How many days apart, for <c>everyNDays</c> only.</param>
/// <param name="StartDate">The date the first occurrence is due, <c>YYYY-MM-DD</c>.</param>
/// <param name="EndDate">The last date an occurrence can be due; left out for a rule that goes on.</param>
/// <param name="Mode"><c>autoPost</c> or <c>remind</c>; left out, each occurrence is posted.</param>
public sealed record CreateRecurringRuleCommand(
    string? Type, Guid AccountId, string? Amount, Guid CategoryId, string? Payee, string? Note, string? Frequency, int? Interval,
    string? StartDate, string? EndDate, string? Mode = null)
    : IRecurringRuleCommand
{
    public RecurringRuleFields Parsed { get; } = RecurringRuleFields.Read(Type, Amount, Frequency, StartDate, EndDate, Mode);
}

public sealed class CreateRecurringRuleValidator : AbstractValidator<CreateRecurringRuleCommand>
{
    public CreateRecurringRuleValidator() => this.RecurringRuleFieldRules();
}

/// <summary>An account or category that is not one of the user's, or is archived, is an error on its id.</summary>
public sealed class CreateRecurringRuleHandler(
    IValidator<CreateRecurringRuleCommand> validator, IRecurringRuleRepository rules, RecurringRuleContext context, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<CreateRecurringRuleCommand, RecurringRuleDetails>(validator)
{
    protected override async Task<Result<RecurringRuleDetails>> ExecuteAsync(CreateRecurringRuleCommand command, CancellationToken cancellationToken)
    {
        var read = await context.ReadAsync(command, current: null, cancellationToken);
        if (!read.IsValid)
        {
            return Result<RecurringRuleDetails>.Invalid(read.Errors);
        }

        var (account, category) = read.Outcome;
        RecurringRule rule;
        try
        {
            rule = RecurringRule.Create(
                command.Parsed.Type!.Value, account.Id, account.Currency, command.Parsed.Amount!.Value, category, command.Payee, command.Note,
                command.Schedule(), clock.GetUtcNow(), command.Parsed.Mode ?? RecurringMode.AutoPost);
        }
        catch (RecurringRuleViolation violation)
        {
            return Result<RecurringRuleDetails>.Invalid(violation.AsErrors());
        }

        rules.Add(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RecurringRuleDetails.Of(rule, account.Currency);
    }
}
