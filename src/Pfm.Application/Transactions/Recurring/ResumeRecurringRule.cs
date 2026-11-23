using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Resumes a paused recurring rule: it posts again from today, in the user's time zone. The occurrences that
/// were due while it was paused are skipped, not posted late. Resuming a rule that is not paused changes nothing.
/// </summary>
public sealed record ResumeRecurringRuleCommand(Guid Id);

public sealed class ResumeRecurringRuleValidator : AbstractValidator<ResumeRecurringRuleCommand>
{
    public ResumeRecurringRuleValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers the resumed rule, or null when the user has no rule with that id.</summary>
public sealed class ResumeRecurringRuleHandler(
    IValidator<ResumeRecurringRuleCommand> validator, IRecurringRuleRepository rules, RecurringRuleContext context, IUserCalendar calendar,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ResumeRecurringRuleCommand, RecurringRuleDetails?>(validator)
{
    protected override Task<Result<RecurringRuleDetails?>> ExecuteAsync(ResumeRecurringRuleCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<RecurringRuleDetails?>>(async () =>
        {
            var rule = await rules.FindAsync(command.Id, cancellationToken);
            if (rule is null)
            {
                return (RecurringRuleDetails?)null;
            }

            rule.Resume(await calendar.TodayAsync(cancellationToken), clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await context.DetailsAsync(rule, cancellationToken);
        });
}
