using Pfm.Domain;

namespace Pfm.Application.Contracts.Transactions;

/// <summary>What other modules may ask about the user's latest and coming transactions. Reports asks it for the dashboard (spec RPT-1).</summary>
public interface IRecentActivity
{
    /// <summary>
    /// The user's latest transactions, as the transaction list starts: newest date first, the last recorded first
    /// within a day. Deleted ones are never among them; each leg of a transfer is one.
    /// </summary>
    /// <param name="count">How many at most.</param>
    Task<IReadOnlyList<RecentTransaction>> LatestAsync(int count, CancellationToken cancellationToken);

    /// <summary>
    /// The occurrences of the user's recurring rules due from today, in their time zone, to <paramref name="days"/>
    /// days after it, earliest first, as the recurring screen lists them (spec REC-3).
    /// </summary>
    Task<IReadOnlyList<UpcomingOccurrence>> UpcomingAsync(int days, CancellationToken cancellationToken);
}

/// <summary>The type of a transaction or of a recurring rule, as another module sees it: it gives the amount its sign.</summary>
public enum ActivityType
{
    Income = 1,
    Expense = 2,
    TransferOut = 3,
    TransferIn = 4,
}

/// <summary>One of the latest transactions.</summary>
/// <param name="Amount">Greater than zero, in <paramref name="Currency"/>, the currency of its account; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="CategoryId">The category of its first line: its only category unless it is split. Null for a leg of a transfer.</param>
/// <param name="TransferId">The transfer a leg belongs to; null for income and expenses.</param>
public sealed record RecentTransaction(
    Guid Id, DateOnly Date, ActivityType Type, Guid AccountId, decimal Amount, Currency Currency, Guid? CategoryId, string? Payee, bool IsRefund,
    bool IsSplit, Guid? TransferId);

/// <summary>One occurrence of a recurring rule still to come.</summary>
/// <param name="Type">Income or an expense; a rule is never a transfer.</param>
/// <param name="Amount">Greater than zero, in <paramref name="Currency"/>, the currency of the rule's account.</param>
/// <param name="IsReminder">The rule reminds, so the occurrence is posted only once the user confirms it; otherwise the worker posts it.</param>
public sealed record UpcomingOccurrence(
    Guid RecurringRuleId, DateOnly DueDate, ActivityType Type, Guid AccountId, decimal Amount, Currency Currency, Guid CategoryId, string? Payee,
    bool IsReminder);
