using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>A transaction as the use cases return it.</summary>
/// <param name="Amount">Greater than zero; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="IsRefund">Whether the expense is a refund.</param>
/// <param name="Currency">The currency of the transaction's account.</param>
/// <param name="CategoryId">The category of the first line: the transaction's only category unless it is split. Null for a leg of a transfer, which has no category.</param>
/// <param name="TransferId">The transfer a Transfer out or Transfer in leg belongs to, shared with its other leg; null for income and expenses.</param>
/// <param name="RowVersion">The version an edit must name.</param>
/// <param name="Lines">One line with the whole amount, or the lines of a split, which sum exactly to the amount.</param>
/// <param name="Tags">The names of its tags, in order.</param>
/// <param name="RecurringRuleId">The recurring rule that posted it; null for one a user recorded, and once the rule is deleted.</param>
public sealed record TransactionDetails(
    Guid Id, TransactionType Type, Guid AccountId, decimal Amount, Currency Currency, DateOnly Date, Guid? CategoryId, string? Payee, string? Note,
    Guid? TransferId, uint RowVersion, bool IsRefund, IReadOnlyList<TransactionLineDetails> Lines, IReadOnlyList<string> Tags,
    Guid? RecurringRuleId = null)
{
    public static TransactionDetails Of(Transaction transaction) =>
        new(transaction.Id, transaction.Type, transaction.AccountId, transaction.Amount, transaction.Currency, transaction.Date,
            transaction.CategoryId, transaction.Payee, transaction.Note, transaction.TransferId, transaction.RowVersion, transaction.IsRefund,
            [.. transaction.Lines.Select(line => new TransactionLineDetails(line.CategoryId, line.Amount, line.Note))],
            [.. transaction.Tags.Select(tag => tag.Name)], transaction.RecurringRuleId);
}

/// <summary>A line of a transaction as the use cases return it.</summary>
/// <param name="Amount">Greater than zero, in the transaction's currency.</param>
public sealed record TransactionLineDetails(Guid CategoryId, decimal Amount, string? Note);
