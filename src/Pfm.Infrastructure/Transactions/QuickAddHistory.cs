using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Reads the user's transactions for quick add. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class QuickAddHistory(PfmDbContext db) : IQuickAddHistory
{
    private const string Escape = "\\";

    public async Task<IReadOnlyList<Guid>> AccountsLastUsedFirstAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>()
            .Where(transaction => transaction.DeletedAt == null)
            .GroupBy(transaction => transaction.AccountId)
            .Select(onAccount => new { AccountId = onAccount.Key, LastRecorded = onAccount.Max(transaction => transaction.CreatedAt) })
            .OrderByDescending(account => account.LastRecorded)
            .Select(account => account.AccountId)
            .ToListAsync(cancellationToken);

    public async Task<Guid?> MostUsedCategoryAsync(string payee, TransactionType? type, CancellationToken cancellationToken)
    {
        // The payee itself in any case, never a pattern: what ILIKE would read as "anything" is escaped.
        var exactly = payee.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

        return await db.Set<Transaction>()
            .Where(transaction => transaction.DeletedAt == null && EF.Functions.ILike(transaction.Payee!, exactly, Escape))
            .Where(transaction => type == null || transaction.Type == type)
            .Join(db.Set<Category>().Where(category => !category.IsArchived), transaction => transaction.CategoryId, category => category.Id, (transaction, _) => transaction)
            .GroupBy(transaction => transaction.CategoryId)
            .Select(withCategory => new { CategoryId = withCategory.Key, Times = withCategory.Count(), LastRecorded = withCategory.Max(transaction => transaction.CreatedAt) })
            .OrderByDescending(category => category.Times)
            .ThenByDescending(category => category.LastRecorded)
            .Select(category => (Guid?)category.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
