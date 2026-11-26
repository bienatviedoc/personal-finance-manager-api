using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>One of the user's reconciliations, with its difference.</summary>
public sealed record GetReconciliation(Guid Id);

public sealed class GetReconciliationHandler(IReconciliationRepository reconciliations, ReconciliationContext context) : IQueryHandler
{
    /// <summary>Null when the user has no reconciliation with that id.</summary>
    public async Task<ReconciliationDetails?> HandleAsync(GetReconciliation query, CancellationToken cancellationToken = default) =>
        await reconciliations.FindAsync(query.Id, cancellationToken) is { } reconciliation ? await context.DetailsAsync(reconciliation, cancellationToken) : null;
}
