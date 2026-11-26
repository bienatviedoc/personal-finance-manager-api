using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// A page of the user's recently deleted transactions: those deleted within the last 30 days, which can still be
/// restored (spec section 3, rule 10). The last deleted comes first. Each leg of a deleted transfer is listed.
/// </summary>
public sealed record ListDeletedTransactions(int Limit = Paging.DefaultLimit, DeletedTransactionCursor? After = null);

/// <summary>A recently deleted transaction, as it was when it was deleted.</summary>
/// <param name="RestorableUntil">When it stops being restorable, and the worker removes it for good soon after.</param>
public sealed record DeletedTransactionDetails(TransactionDetails Transaction, DateTimeOffset DeletedAt, DateTimeOffset RestorableUntil)
{
    public static DeletedTransactionDetails Of(Domain.Transactions.Transaction transaction) =>
        new(TransactionDetails.Of(transaction), transaction.DeletedAt!.Value, transaction.DeletedAt.Value + Domain.Transactions.Transaction.RecoveryPeriod);
}

public sealed class ListDeletedTransactionsHandler(ITransactionRepository transactions, TimeProvider clock) : IQueryHandler
{
    public async Task<Page<DeletedTransactionDetails>> HandleAsync(ListDeletedTransactions query, CancellationToken cancellationToken = default)
    {
        // One more than the page holds says whether another page follows.
        var found = await transactions.ListDeletedAsync(
            clock.GetUtcNow() - Transaction.RecoveryPeriod, query.Limit + 1, query.After, cancellationToken);
        var next = found.Count > query.Limit ? DeletedTransactionCursor.At(found[query.Limit - 1]).ToString() : null;
        return new Page<DeletedTransactionDetails>([.. found.Take(query.Limit).Select(DeletedTransactionDetails.Of)], next);
    }
}

/// <summary>Where a page of recently deleted transactions ends: when its last one was deleted, and its id; opaque to clients.</summary>
public sealed record DeletedTransactionCursor(DateTimeOffset DeletedAt, Guid Id)
{
    public static DeletedTransactionCursor At(Transaction transaction) => new(transaction.DeletedAt!.Value, transaction.Id);

    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out DeletedTransactionCursor? parsed)
    {
        parsed = null;
        if (cursor is null || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        if (text.Length <= 32
            || !Guid.TryParseExact(text[..32], "N", out var id)
            || !long.TryParse(text[32..], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        parsed = new DeletedTransactionCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    public override string ToString() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{Id:N}{DeletedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}"));
}
