using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Transactions.Recurring;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>Answers other modules' questions about the user's latest and coming transactions.</summary>
public sealed class RecentActivity(ITransactionRepository transactions, ListUpcomingOccurrencesHandler upcoming) : IRecentActivity
{
    public async Task<IReadOnlyList<RecentTransaction>> LatestAsync(int count, CancellationToken cancellationToken) =>
        [.. (await transactions.ListAsync(TransactionFilter.None, TransactionSort.NewestFirst, count, null, cancellationToken))
            .Select(transaction => new RecentTransaction(
                transaction.Id, transaction.Date, ActivityTypeOf(transaction.Type), transaction.AccountId, transaction.Amount, transaction.Currency,
                transaction.CategoryId, transaction.Payee, transaction.IsRefund, transaction.Lines.Count > 1, transaction.TransferId))];

    public async Task<IReadOnlyList<UpcomingOccurrence>> UpcomingAsync(int days, CancellationToken cancellationToken) =>
        [.. (await upcoming.HandleAsync(new ListUpcomingOccurrences(days), cancellationToken))
            .Select(occurrence => new UpcomingOccurrence(
                occurrence.RecurringRuleId, occurrence.DueDate, ActivityTypeOf(occurrence.Type), occurrence.AccountId, occurrence.Amount, occurrence.Currency,
                occurrence.CategoryId, occurrence.Payee, occurrence.Mode == RecurringMode.Remind))];

    private static ActivityType ActivityTypeOf(TransactionType type) => type switch
    {
        TransactionType.Income => ActivityType.Income,
        TransactionType.Expense => ActivityType.Expense,
        TransactionType.TransferOut => ActivityType.TransferOut,
        TransactionType.TransferIn => ActivityType.TransferIn,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };
}
