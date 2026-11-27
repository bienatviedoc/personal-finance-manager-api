using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>Deletes a recurring rule: it posts nothing more. The transactions it posted stay, no longer linked to a rule.</summary>
public sealed record DeleteRecurringRuleCommand(Guid Id);

public sealed class DeleteRecurringRuleValidator : AbstractValidator<DeleteRecurringRuleCommand>
{
    public DeleteRecurringRuleValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no rule with that id. The delete is recorded in the user's audit log in the same save.</summary>
public sealed class DeleteRecurringRuleHandler(
    IValidator<DeleteRecurringRuleCommand> validator, IRecurringRuleRepository rules, IAccountLookup accounts, IAuditLog auditLog, IUnitOfWork unitOfWork)
    : CommandHandler<DeleteRecurringRuleCommand, Done?>(validator)
{
    /// <summary>A rule the worker posts at the same moment is deleted as posted; one deleted by another request is no longer found.</summary>
    protected override Task<Result<Done?>> ExecuteAsync(DeleteRecurringRuleCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<Done?>>(async () =>
        {
            var rule = await rules.FindAsync(command.Id, cancellationToken);
            if (rule is null)
            {
                return (Done?)null;
            }

            rules.Remove(rule);
            var account = await accounts.FindAsync(rule.AccountId, cancellationToken)
                ?? throw new InvalidOperationException($"Recurring rule {rule.Id} is on account {rule.AccountId}, which was not found.");
            await auditLog.RecurringRuleDeletedAsync(
                rule.Id, rule.Type == Domain.Transactions.TransactionType.Income, rule.Amount, account.Currency, rule.Payee, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new Done();
        });
}
