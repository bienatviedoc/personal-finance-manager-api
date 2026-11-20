using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions;

/// <summary>
/// A page of the user's transactions, newest date first and, within a date, last recorded first, starting
/// after <paramref name="After"/>. Deleted ones are never in it.
/// </summary>
public sealed record ListTransactions(int Limit = Paging.DefaultLimit, TransactionCursor? After = null);

public sealed class ListTransactionsHandler(ITransactionRepository transactions) : IQueryHandler
{
    public async Task<Page<TransactionDetails>> HandleAsync(ListTransactions query, CancellationToken cancellationToken = default)
    {
        // One more than the page holds says whether another page follows.
        var found = await transactions.ListAsync(query.Limit + 1, query.After, cancellationToken);
        var next = found.Count > query.Limit ? new TransactionCursor(found[query.Limit - 1].Date, found[query.Limit - 1].Id).ToString() : null;
        return new Page<TransactionDetails>([.. found.Take(query.Limit).Select(TransactionDetails.Of)], next);
    }
}

/// <summary>
/// Where a page of transactions ends: the date and id of its last one, opaque to clients. Keyed on both, so a
/// page never repeats or skips a transaction that was there before, even when others are recorded in between.
/// </summary>
public sealed record TransactionCursor(DateOnly Date, Guid Id)
{
    private const string DateFormat = "yyyyMMdd";

    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out TransactionCursor? parsed)
    {
        parsed = null;
        if (cursor is null || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        if (text.Length != DateFormat.Length + 32
            || !DateOnly.TryParseExact(text[..DateFormat.Length], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !Guid.TryParseExact(text[DateFormat.Length..], "N", out var id))
        {
            return false;
        }

        parsed = new TransactionCursor(date, id);
        return true;
    }

    public override string ToString() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{Date.ToString(DateFormat, CultureInfo.InvariantCulture)}{Id:N}"));
}
