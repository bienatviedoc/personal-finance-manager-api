using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>
/// Completes a reconciliation, locking its ticked transactions (spec section 3, rule 11). It completes only when
/// the difference is exactly zero; nothing is ever added to make it so. Completing one already completed changes
/// nothing.
/// </summary>
public sealed record CompleteReconciliationCommand(Guid Id);

public sealed class CompleteReconciliationValidator : AbstractValidator<CompleteReconciliationCommand>
{
    public CompleteReconciliationValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>What completing a reconciliation came to.</summary>
public abstract record CompleteReconciliationOutcome
{
    private CompleteReconciliationOutcome()
    {
    }

    /// <summary>Completed: its transactions are locked.</summary>
    public sealed record Completed(ReconciliationDetails Reconciliation) : CompleteReconciliationOutcome;

    /// <summary>The difference is not zero; nothing changed.</summary>
    public sealed record DifferenceNotZero(decimal Difference) : CompleteReconciliationOutcome;
}

/// <summary>Answers null when the user has no reconciliation with that id.</summary>
public sealed class CompleteReconciliationHandler(
    IValidator<CompleteReconciliationCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CompleteReconciliationCommand, CompleteReconciliationOutcome?>(validator)
{
    /// <summary>
    /// Locking a transaction saves it, so an edit or a tick saved at the same moment fails one of the two saves,
    /// and the completion is made again on what was saved: the difference it checks is the one it locks.
    /// </summary>
    protected override Task<Result<CompleteReconciliationOutcome?>> ExecuteAsync(CompleteReconciliationCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<CompleteReconciliationOutcome?>>(async () =>
        {
            if (await reconciliations.FindAsync(command.Id, cancellationToken) is not { } reconciliation)
            {
                return (CompleteReconciliationOutcome?)null;
            }

            if (!reconciliation.IsCompleted)
            {
                var (_, reconciledBalance) = await context.ReconciledBalanceAsync(reconciliation, cancellationToken);
                var ticked = await transactions.ListTickedAsync(reconciliation.Id, cancellationToken);
                var difference = reconciliation.DifferenceWith(reconciledBalance, ticked);
                if (difference != 0)
                {
                    return new CompleteReconciliationOutcome.DifferenceNotZero(difference);
                }

                reconciliation.Complete(reconciledBalance, ticked, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new CompleteReconciliationOutcome.Completed(await context.DetailsAsync(reconciliation, cancellationToken));
        });
}
