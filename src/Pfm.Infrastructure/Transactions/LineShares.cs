using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

/// <summary>Queries of <see cref="LineShare"/>s.</summary>
internal static class LineShares
{
    /// <summary>The lines that count: those of transactions not deleted, dated between two days (either can be left open), on one account or on all.</summary>
    public static IQueryable<LineShare> NotDeletedBetween(this IQueryable<LineShare> shares, DateOnly? from, DateOnly? to, Guid? accountId)
    {
        shares = shares.Where(share => share.DeletedAt == null);
        if (from is { } first)
        {
            shares = shares.Where(share => share.Date >= first);
        }

        if (to is { } last)
        {
            shares = shares.Where(share => share.Date <= last);
        }

        return accountId is { } account ? shares.Where(share => share.AccountId == account) : shares;
    }

    /// <summary>Each transaction's lines added up: what its lines among these come to in the base currency, zero while it is not converted.</summary>
    public static IQueryable<TransactionShare> SumByTransaction(this IQueryable<LineShare> shares) =>
        shares
            .GroupBy(share => new { share.TransactionId, share.Date, share.Type, share.IsRefund, IsConverted = share.BaseCurrencyAmount != null })
            .Select(transaction => new TransactionShare(
                transaction.Key.TransactionId, transaction.Key.Date, transaction.Key.Type, transaction.Key.IsRefund, transaction.Key.IsConverted,
                transaction.Sum(share => share.BaseCurrencyAmount ?? 0)));
}

/// <summary>What some of a transaction's lines come to in the base currency, without a sign.</summary>
internal sealed record TransactionShare(Guid TransactionId, DateOnly Date, TransactionType Type, bool IsRefund, bool IsConverted, decimal Amount)
{
    /// <summary>What it brought in: the amount of an income, zero for anything else.</summary>
    public decimal IncomeAmount => Type == TransactionType.Income ? Amount : 0;

    /// <summary>What it moved spent by, with spent's one sign (<see cref="Spent.Signed"/>): zero for anything but an expense.</summary>
    public decimal SpentAmount => Spent.Signed(Type, Amount, IsRefund);
}
