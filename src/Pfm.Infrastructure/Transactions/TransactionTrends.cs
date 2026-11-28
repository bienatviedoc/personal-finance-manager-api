using Microsoft.EntityFrameworkCore;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Reads the user's transactions for a trend. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class TransactionTrends(PfmDbContext db) : ITransactionTrends
{
    /// <summary>
    /// The database adds up each transaction's lines on the category and its subcategories (every line when there is
    /// no category) in the base currency (<see cref="LineShare"/>, shared as <see cref="Transaction.SpentAmountIn"/>
    /// shares them); what each spent is worked out from those sums here, by the sign of spent's one definition
    /// (<see cref="Spent.Signed"/>).
    /// </summary>
    public async Task<IReadOnlyList<TrendTransaction>> AmountsAsync(
        DateOnly from, DateOnly to, TrendType type, Guid? categoryId, string? payee, Guid? accountId, CancellationToken cancellationToken)
    {
        var transactionType = type == TrendType.Income ? TransactionType.Income : TransactionType.Expense;
        var shares = db.Set<LineShare>().Counted(from, to, accountId).Where(share => share.Type == transactionType);
        if (categoryId is { } category)
        {
            var withSubcategories = db.Set<Category>().AndSubcategoriesOf(category);
            shares = shares.Where(share => withSubcategories.Contains(share.CategoryId));
        }

        if (payee is not null)
        {
            var exactly = LikePattern.Exactly(payee);
            shares = shares.Where(share => EF.Functions.ILike(share.Payee!, exactly, LikePattern.Escape));
        }

        return
        [
            .. (await shares.SumByTransaction().ToListAsync(cancellationToken)).Select(sum => new TrendTransaction(
                sum.TransactionId,
                sum.Date,
                !sum.IsConverted ? null : sum.Type == TransactionType.Income ? sum.Amount : Spent.Signed(sum.Type, sum.Amount, sum.IsRefund))),
        ];
    }
}
