using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>One of the user's transfers, by the transfer id its two legs share.</summary>
public sealed record GetTransfer(Guid Id);

public sealed class GetTransferHandler(ITransactionRepository transactions) : IQueryHandler
{
    /// <summary>Null when the user has no transfer with that id, or deleted it.</summary>
    public async Task<TransferDetails?> HandleAsync(GetTransfer query, CancellationToken cancellationToken = default) =>
        await transactions.FindTransferAsync(query.Id, deletedToo: false, cancellationToken) is { } transfer ? TransferDetails.Of(transfer) : null;
}
