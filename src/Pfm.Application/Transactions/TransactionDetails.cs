using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>A transaction as the use cases return it.</summary>
/// <param name="Amount">Greater than zero; the type gives the sign.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="RowVersion">The version an edit must name.</param>
/// <param name="RecurringRuleId">The recurring rule that posted it; null for one a user recorded, and once the rule is deleted.</param>
public sealed record TransactionDetails(
    Guid Id, TransactionType Type, Guid AccountId, decimal Amount, Currency Currency, DateOnly Date, Guid CategoryId, string? Payee, string? Note,
    uint RowVersion, Guid? RecurringRuleId = null)
{
    public static TransactionDetails Of(Transaction transaction) =>
        new(transaction.Id, transaction.Type, transaction.AccountId, transaction.Amount, transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.RowVersion, transaction.RecurringRuleId);
}
