using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

/// <summary>
/// One line of a transaction as the database adds up spent and income (spec section 6; section 8): the line's
/// share of the transaction's amount in the base currency, beside what a sum is filtered and grouped by. Read
/// from the view <see cref="View"/>, which shares a split's converted amount among its lines as
/// <see cref="Transaction.SpentAmountIn"/> does: each line's share rounded half away from zero to the base
/// currency's decimals, and the last line taking what is left. A share has no sign: what it does to spent is
/// <see cref="Spent.Signed"/>, applied to the sums. Never written: it is the transactions' own rows, seen another way.
/// User-owned, so the context's user filter keeps every query to the signed-in user's lines.
/// </summary>
internal sealed class LineShare : IUserOwned
{
    public const string View = "TransactionLineShares";

    /// <summary>For persistence, which sets every property itself.</summary>
    private LineShare()
    {
    }

    public Guid UserId { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    public DateOnly Date { get; private set; }

    public TransactionType Type { get; private set; }

    public bool IsRefund { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public string? Payee { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>The line's share of the transaction's amount in the base currency; null while the transaction is not converted.</summary>
    public decimal? BaseCurrencyAmount { get; private set; }
}

internal sealed class LineShareConfiguration : IEntityTypeConfiguration<LineShare>
{
    public void Configure(EntityTypeBuilder<LineShare> share)
    {
        share.HasNoKey().ToView(LineShare.View);
        share.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        share.Property(row => row.BaseCurrencyAmount).HasPrecision(19, 4);
    }
}

/// <summary>The lines that count: those of transactions not deleted, dated between two days, on one account or on all.</summary>
internal static class LineShares
{
    public static IQueryable<LineShare> Counted(this IQueryable<LineShare> shares, DateOnly? from, DateOnly? to, Guid? accountId)
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
internal sealed record TransactionShare(Guid TransactionId, DateOnly Date, TransactionType Type, bool IsRefund, bool IsConverted, decimal Amount);
