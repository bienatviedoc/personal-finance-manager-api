using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>A transaction as the use cases return it.</summary>
/// <param name="Amount">Greater than zero; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="IsRefund">Whether the expense is a refund.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="RowVersion">The version an edit must name.</param>
public sealed record TransactionDetails(
    Guid Id, TransactionType Type, Guid AccountId, decimal Amount, Currency Currency, DateOnly Date, Guid CategoryId, string? Payee, string? Note,
    uint RowVersion, bool IsRefund = false)
{
    public static TransactionDetails Of(Transaction transaction) =>
        new(transaction.Id, transaction.Type, transaction.AccountId, transaction.Amount, transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.RowVersion, transaction.IsRefund);
}
