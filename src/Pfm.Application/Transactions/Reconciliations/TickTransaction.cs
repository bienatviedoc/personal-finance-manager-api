using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>Ticks a transaction the statement shows. Ticking one already ticked changes nothing.</summary>
public sealed record TickTransactionCommand(Guid ReconciliationId, Guid TransactionId) : ITickCommand;

public sealed class TickTransactionValidator : AbstractValidator<TickTransactionCommand>
{
    public TickTransactionValidator()
    {
        RuleFor(command => command.ReconciliationId).Required();
        RuleFor(command => command.TransactionId).Required();
    }
}

public sealed class TickTransactionHandler(
    IValidator<TickTransactionCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : TickHandler<TickTransactionCommand>(validator, reconciliations, transactions, context, unitOfWork, clock)
{
    protected override void Change(Reconciliation reconciliation, Transaction transaction, DateTimeOffset now) =>
        reconciliation.Tick(transaction, now);
}
