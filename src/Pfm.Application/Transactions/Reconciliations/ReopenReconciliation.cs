using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>
/// Reopens a completed reconciliation: its transactions are unlocked and stay ticked, to be changed and completed
/// again. Only the account's latest reconciliation can be reopened, since each later one is measured from what
/// the ones before it reconciled. Reopening one in progress changes nothing.
/// </summary>
public sealed record ReopenReconciliationCommand(Guid Id);

public sealed class ReopenReconciliationValidator : AbstractValidator<ReopenReconciliationCommand>
{
    public ReopenReconciliationValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>What reopening came to: the reconciliation, or nothing changed because a later one exists.</summary>
public sealed record ReopenReconciliationOutcome(ReconciliationDetails? Reopened)
{
    /// <summary>The account has a later reconciliation; nothing changed.</summary>
    public static readonly ReopenReconciliationOutcome NotLatest = new((ReconciliationDetails?)null);
}

/// <summary>Answers null when the user has no reconciliation with that id.</summary>
public sealed class ReopenReconciliationHandler(
    IValidator<ReopenReconciliationCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ReopenReconciliationCommand, ReopenReconciliationOutcome?>(validator)
{
    protected override Task<Result<ReopenReconciliationOutcome?>> ExecuteAsync(ReopenReconciliationCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<ReopenReconciliationOutcome?>>(async () =>
        {
            if (await reconciliations.FindAsync(command.Id, cancellationToken) is not { } reconciliation)
            {
                return (ReopenReconciliationOutcome?)null;
            }

            if (reconciliation.IsCompleted)
            {
                if ((await reconciliations.FindLatestAsync(reconciliation.AccountId, cancellationToken))?.Id != reconciliation.Id)
                {
                    return ReopenReconciliationOutcome.NotLatest;
                }

                reconciliation.Reopen(await transactions.ListTickedAsync(reconciliation.Id, cancellationToken), clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new ReopenReconciliationOutcome(await context.DetailsAsync(reconciliation, cancellationToken));
        });
}
