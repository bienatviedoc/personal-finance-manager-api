using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Confirms a reminder (spec REC-2): its transaction is recorded, dated on its due date, saying what the rule
/// says but for an amount the user adjusted, and the rule moves on. A rule's reminders are settled earliest first.
/// </summary>
/// <param name="DueDate">The reminder's due date, <c>YYYY-MM-DD</c>.</param>
/// <param name="Amount">A decimal string greater than zero, in the currency of the rule's account; left out for the rule's own amount.</param>
public sealed record ConfirmReminderCommand(Guid RecurringRuleId, string? DueDate, string? Amount = null)
{
    public DateOnly? ParsedDueDate { get; } = DateText.Read(DueDate);
}

public sealed class ConfirmReminderValidator : AbstractValidator<ConfirmReminderCommand>
{
    public ConfirmReminderValidator()
    {
        RuleFor(command => command.RecurringRuleId).Required();
        RuleFor(command => command.DueDate).Must((command, _) => command.ParsedDueDate is not null).WithMessage(DateText.Message);
        RuleFor(command => command.Amount)
            .Must(amount => amount is null || (AmountText.TryParse(amount, out var parsed) && parsed > 0))
            .WithMessage("Enter an amount greater than zero as a decimal string such as \"125000.00\", or leave it out for the rule's own.");
    }
}

/// <summary>
/// Answers the rule as confirming left it; null when the user has no such rule, or it has no reminder due on
/// that date. What would refuse a transaction the user records (an archived account or category, an amount
/// the currency cannot hold) refuses it, keyed by the transaction's field.
/// </summary>
public sealed class ConfirmReminderHandler(
    IValidator<ConfirmReminderCommand> validator, IRecurringRuleRepository rules, CreateTransactionHandler recordTransaction, RecurringRuleContext context,
    IUserCalendar calendar, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ConfirmReminderCommand, RecurringRuleDetails?>(validator)
{
    protected override async Task<Result<RecurringRuleDetails?>> ExecuteAsync(ConfirmReminderCommand command, CancellationToken cancellationToken)
    {
        var dueDate = command.ParsedDueDate!.Value;
        var today = await calendar.TodayAsync(cancellationToken);
        var rule = await rules.FindAsync(command.RecurringRuleId, cancellationToken);
        if (rule is null || !rule.HasReminder(dueDate, today))
        {
            return (RecurringRuleDetails?)null;
        }

        if (rule.ReminderOrderBroken(dueDate) is { } orderRule)
        {
            return Result<RecurringRuleDetails?>.Invalid(FieldError.On(nameof(ConfirmReminderCommand.DueDate), orderRule));
        }

        // Under the idempotency key of the occurrence: confirmed twice at once, it is recorded once.
        var recorded = await recordTransaction.HandleAsync(RecurringOccurrence.TransactionOf(rule, dueDate, command.Amount), cancellationToken);
        if (!recorded.IsValid)
        {
            return Result<RecurringRuleDetails?>.Invalid(recorded.Errors);
        }

        // Forget what recording read, and settle the reminder on the rule as it is saved now.
        unitOfWork.DiscardChanges();
        return await unitOfWork.RetryOnConcurrentChangeAsync<Result<RecurringRuleDetails?>>(async () =>
        {
            var current = await rules.FindAsync(command.RecurringRuleId, cancellationToken);
            if (current is null)
            {
                return (RecurringRuleDetails?)null;
            }

            if (current.NextRunDate == dueDate && current.HasReminder(dueDate, today))
            {
                current.Confirmed(dueDate, today, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return await context.DetailsAsync(current, cancellationToken);
        });
    }
}

/// <summary>Dismisses a reminder (spec REC-2): it is settled with no transaction, and the rule moves on. A rule's reminders are settled earliest first.</summary>
/// <param name="DueDate">The reminder's due date, <c>YYYY-MM-DD</c>.</param>
public sealed record DismissReminderCommand(Guid RecurringRuleId, string? DueDate)
{
    public DateOnly? ParsedDueDate { get; } = DateText.Read(DueDate);
}

public sealed class DismissReminderValidator : AbstractValidator<DismissReminderCommand>
{
    public DismissReminderValidator()
    {
        RuleFor(command => command.RecurringRuleId).Required();
        RuleFor(command => command.DueDate).Must((command, _) => command.ParsedDueDate is not null).WithMessage(DateText.Message);
    }
}

/// <summary>Answers the rule as dismissing left it; null when the user has no such rule, or it has no reminder due on that date.</summary>
public sealed class DismissReminderHandler(
    IValidator<DismissReminderCommand> validator, IRecurringRuleRepository rules, RecurringRuleContext context, IUserCalendar calendar,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<DismissReminderCommand, RecurringRuleDetails?>(validator)
{
    protected override async Task<Result<RecurringRuleDetails?>> ExecuteAsync(DismissReminderCommand command, CancellationToken cancellationToken)
    {
        var dueDate = command.ParsedDueDate!.Value;
        var today = await calendar.TodayAsync(cancellationToken);
        return await unitOfWork.RetryOnConcurrentChangeAsync<Result<RecurringRuleDetails?>>(async () =>
        {
            var rule = await rules.FindAsync(command.RecurringRuleId, cancellationToken);
            if (rule is null || !rule.HasReminder(dueDate, today))
            {
                return (RecurringRuleDetails?)null;
            }

            if (rule.ReminderOrderBroken(dueDate) is { } orderRule)
            {
                return Result<RecurringRuleDetails?>.Invalid(FieldError.On(nameof(DismissReminderCommand.DueDate), orderRule));
            }

            rule.Dismiss(dueDate, today, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await context.DetailsAsync(rule, cancellationToken);
        });
    }
}
