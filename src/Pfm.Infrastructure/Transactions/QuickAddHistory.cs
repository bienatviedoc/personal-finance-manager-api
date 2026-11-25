using Microsoft.EntityFrameworkCore;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Reads the user's transactions for quick add. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class QuickAddHistory(PfmDbContext db) : IQuickAddHistory
{
    public async Task<IReadOnlyList<Guid>> AccountsLastUsedFirstAsync(CancellationToken cancellationToken) =>
        await db.Set<Transaction>()
            // Income and expenses only: a transfer is not what quick add records, and its two legs are on two accounts at once.
            .Where(transaction => transaction.DeletedAt == null && transaction.TransferId == null)
            .GroupBy(transaction => transaction.AccountId)
            .Select(onAccount => new { AccountId = onAccount.Key, LastRecorded = onAccount.Max(transaction => transaction.CreatedAt) })
            .OrderByDescending(account => account.LastRecorded)
            .Select(account => account.AccountId)
            .ToListAsync(cancellationToken);

    public async Task<Guid?> MostUsedCategoryAsync(string payee, TransactionType? type, CancellationToken cancellationToken)
    {
        var exactly = LikePattern.Exactly(payee);

        return await db.Set<Transaction>()
            .Where(transaction => transaction.DeletedAt == null && EF.Functions.ILike(transaction.Payee!, exactly, LikePattern.Escape))
            .Where(transaction => type == null || transaction.Type == type)
            // A refund is money coming back: it says nothing about what the payee is usually paid for.
            .Where(transaction => !transaction.IsRefund)
            // Each line of a split is a use of its own category.
            .SelectMany(transaction => transaction.Lines.Select(line => new { line.CategoryId, transaction.CreatedAt }))
            .Join(db.Set<Category>().Where(category => !category.IsArchived), line => line.CategoryId, category => category.Id, (line, _) => line)
            .GroupBy(line => line.CategoryId)
            .Select(withCategory => new { CategoryId = withCategory.Key, Times = withCategory.Count(), LastRecorded = withCategory.Max(line => line.CreatedAt) })
            .OrderByDescending(category => category.Times)
            .ThenByDescending(category => category.LastRecorded)
            .Select(category => (Guid?)category.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
