using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

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
