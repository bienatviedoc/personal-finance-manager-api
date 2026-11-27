using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>Deletes a categorisation rule: it categorises nothing more. What it categorised keeps its category.</summary>
public sealed record DeleteCategorisationRuleCommand(Guid Id);

public sealed class DeleteCategorisationRuleValidator : AbstractValidator<DeleteCategorisationRuleCommand>
{
    public DeleteCategorisationRuleValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no rule with that id. The delete is recorded in the user's audit log in the same save.</summary>
public sealed class DeleteCategorisationRuleHandler(
    IValidator<DeleteCategorisationRuleCommand> validator, ICategorisationRuleRepository rules, IAuditLog auditLog, IUnitOfWork unitOfWork)
    : CommandHandler<DeleteCategorisationRuleCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(DeleteCategorisationRuleCommand command, CancellationToken cancellationToken)
    {
        if (await rules.FindAsync(command.Id, cancellationToken) is not { } rule)
        {
            return (Done?)null;
        }

        rules.Remove(rule);
        await auditLog.CategorisationRuleDeletedAsync(rule.Id, rule.MatchText, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Done();
    }
}
