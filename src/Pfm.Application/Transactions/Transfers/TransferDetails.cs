using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>A transfer as the use cases return it: both legs as one movement.</summary>
/// <param name="Id">The transfer id its two legs share.</param>
/// <param name="Amount">What left the source account, in <paramref name="Currency"/>.</param>
/// <param name="ToAmount">What arrived on the destination account, in <paramref name="ToCurrency"/>.</param>
/// <param name="OutTransactionId">The Transfer out leg, as the transaction list shows it.</param>
/// <param name="InTransactionId">The Transfer in leg, as the transaction list shows it.</param>
/// <param name="RowVersion">The version an edit must name (<see cref="Transfer.RowVersion"/>).</param>
public sealed record TransferDetails(
    Guid Id, Guid FromAccountId, Guid ToAccountId, decimal Amount, Currency Currency, decimal ToAmount, Currency ToCurrency, DateOnly Date, string? Note,
    Guid OutTransactionId, Guid InTransactionId, uint RowVersion)
{
    public static TransferDetails Of(Transfer transfer) =>
        new(transfer.Id, transfer.FromAccountId, transfer.ToAccountId, transfer.Amount, transfer.OutLeg.Currency, transfer.ToAmount, transfer.InLeg.Currency,
            transfer.Date, transfer.Note, transfer.OutLeg.Id, transfer.InLeg.Id, transfer.RowVersion);
}
