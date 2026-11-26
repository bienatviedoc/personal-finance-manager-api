using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>Unticks a transaction. Unticking one not ticked changes nothing.</summary>
public sealed record UntickTransactionCommand(Guid ReconciliationId, Guid TransactionId) : ITickCommand;

public sealed class UntickTransactionValidator : AbstractValidator<UntickTransactionCommand>
{
    public UntickTransactionValidator()
    {
        RuleFor(command => command.ReconciliationId).Required();
        RuleFor(command => command.TransactionId).Required();
    }
}

public sealed class UntickTransactionHandler(
    IValidator<UntickTransactionCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : TickHandler<UntickTransactionCommand>(validator, reconciliations, transactions, context, unitOfWork, clock)
{
    protected override void Change(Reconciliation reconciliation, Transaction transaction, DateTimeOffset now) =>
        reconciliation.Untick(transaction, now);
}
