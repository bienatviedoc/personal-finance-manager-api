using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>Pauses a recurring rule: it posts nothing until it is resumed. Pausing a paused rule changes nothing.</summary>
public sealed record PauseRecurringRuleCommand(Guid Id);

public sealed class PauseRecurringRuleValidator : AbstractValidator<PauseRecurringRuleCommand>
{
    public PauseRecurringRuleValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers the paused rule, or null when the user has no rule with that id.</summary>
public sealed class PauseRecurringRuleHandler(
    IValidator<PauseRecurringRuleCommand> validator, IRecurringRuleRepository rules, RecurringRuleContext context, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<PauseRecurringRuleCommand, RecurringRuleDetails?>(validator)
{
    protected override Task<Result<RecurringRuleDetails?>> ExecuteAsync(PauseRecurringRuleCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<RecurringRuleDetails?>>(async () =>
        {
            var rule = await rules.FindAsync(command.Id, cancellationToken);
            if (rule is null)
            {
                return (RecurringRuleDetails?)null;
            }

            rule.Pause(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await context.DetailsAsync(rule, cancellationToken);
        });
}
