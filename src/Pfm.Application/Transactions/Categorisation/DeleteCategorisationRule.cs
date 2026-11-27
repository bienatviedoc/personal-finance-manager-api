using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Categorisation;

/// <summary>Deletes a categorisation rule: it categorises nothing more. What it categorised keeps its category.</summary>
public sealed record DeleteCategorisationRuleCommand(Guid Id);

public sealed class DeleteCategorisationRuleValidator : AbstractValidator<DeleteCategorisationRuleCommand>
{
    public DeleteCategorisationRuleValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no rule with that id.</summary>
public sealed class DeleteCategorisationRuleHandler(
    IValidator<DeleteCategorisationRuleCommand> validator, ICategorisationRuleRepository rules, IUnitOfWork unitOfWork)
    : CommandHandler<DeleteCategorisationRuleCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(DeleteCategorisationRuleCommand command, CancellationToken cancellationToken)
    {
        if (await rules.FindAsync(command.Id, cancellationToken) is not { } rule)
        {
            return (Done?)null;
        }

        rules.Remove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Done();
    }
}
