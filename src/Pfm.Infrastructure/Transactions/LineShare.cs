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

    /// <summary>The currency of the transaction, which its line's own amount is in.</summary>
    public Currency Currency { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>The line's own part of the transaction's amount, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The line's share of the transaction's amount in the base currency; null while the transaction is not converted.</summary>
    public decimal? BaseCurrencyAmount { get; private set; }
}
