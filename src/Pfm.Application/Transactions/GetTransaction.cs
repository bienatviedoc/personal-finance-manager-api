using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions;

/// <summary>One of the user's transactions.</summary>
public sealed record GetTransaction(Guid Id);

public sealed class GetTransactionHandler(ITransactionRepository transactions) : IQueryHandler
{
    /// <summary>Null when the user has no transaction with that id, or deleted it.</summary>
    public async Task<TransactionDetails?> HandleAsync(GetTransaction query, CancellationToken cancellationToken = default) =>
        await transactions.FindAsync(query.Id, cancellationToken) is { } transaction ? TransactionDetails.Of(transaction) : null;
}
