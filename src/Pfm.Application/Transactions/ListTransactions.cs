using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// A page of the user's transactions that pass <paramref name="Filter"/>, in the order of
/// <paramref name="Sort"/>, starting after <paramref name="After"/>, with the total of all that pass. Deleted
/// ones are never in it.
/// </summary>
public sealed record ListTransactions(
    TransactionFilter Filter, TransactionSort Sort, int Limit = Paging.DefaultLimit, TransactionCursor? After = null);

/// <summary>
/// Which transactions to list (spec TXN-2). Everything left out lets every transaction through.
/// </summary>
/// <param name="From">The earliest date listed.</param>
/// <param name="To">The latest date listed.</param>
/// <param name="CategoryId">A category: transactions with a line on it or on one of its subcategories are listed, so a split is found by each of its categories.</param>
/// <param name="Min">The smallest amount listed, in whatever currency the transaction is in.</param>
/// <param name="Max">The largest amount listed, in whatever currency the transaction is in.</param>
/// <param name="Search">Text the payee, the note or the note of any line contains, in upper or lower case.</param>
public sealed record TransactionFilter(
    DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null, Guid? CategoryId = null, TransactionType? Type = null,
    decimal? Min = null, decimal? Max = null, string? Search = null)
{
    public const int MaxSearchLength = 200;

    public static readonly TransactionFilter None = new();
}

/// <summary>What a list of transactions is sorted by (spec TXN-3).</summary>
public enum TransactionSortBy
{
    Date = 1,
    Amount = 2,
}

public enum SortOrder
{
    Asc = 1,
    Desc = 2,
}

/// <summary>
/// The order of a list of transactions. Within one date, or one amount, the last recorded comes first when
/// descending and last when ascending, so the two orders are each other's reverse.
/// </summary>
public sealed record TransactionSort(TransactionSortBy By, SortOrder Order)
{
    /// <summary>Newest date first.</summary>
    public static readonly TransactionSort NewestFirst = new(TransactionSortBy.Date, SortOrder.Desc);
}

/// <summary>A page of transactions, with the total of every transaction that passed the filter, not only of the page.</summary>
public sealed record TransactionList(Page<TransactionDetails> Page, FilteredTotal Total);

/// <summary>
/// What the transactions that pass a filter come to (spec TXN-3): how many there are, and their amounts added
/// up in each currency. Currencies are never added together. Filtered by a category, each transaction is
/// counted once and adds only its lines in that category and its subcategories, as spent does (spec section 6):
/// a split is listed whole but counts only the part it spent there.
/// </summary>
/// <param name="Spent">
/// Filtered by a category, what the transactions spent in it and its subcategories, in the base currency: spent
/// as a budget counts it (spec section 6), so the transactions behind a budget come to its spent (spec BUD-8).
/// Null when the list is not filtered by a category.
/// </param>
public sealed record FilteredTotal(int Count, IReadOnlyList<CurrencyTotal> Currencies, SpentTotal? Spent = null);

/// <summary>What the filtered transactions spent in a category and its subcategories, by the one definition of spent.</summary>
/// <param name="Currency">The user's base currency.</param>
/// <param name="Amount">The expenses minus the refunds, each converted at the rate of its own date, a split counting only its lines in the categories. It can be negative.</param>
/// <param name="NotConverted">How many of the expenses are not converted, and so are left out of <paramref name="Amount"/>.</param>
public sealed record SpentTotal(Currency Currency, decimal Amount, int NotConverted);

/// <summary>The filtered transactions in one currency, added up.</summary>
/// <param name="Income">The income among them, added up.</param>
/// <param name="Expenses">The expenses among them, added up, minus the refunds: below zero when more came back than was spent.</param>
/// <param name="Net">What they do to balances together: each amount with the sign of its type.</param>
public sealed record CurrencyTotal(Currency Currency, decimal Income, decimal Expenses, decimal Net);

public sealed class ListTransactionsHandler(ITransactionRepository transactions, ICategoryRepository categories, IBaseCurrency baseCurrency) : IQueryHandler
{
    public async Task<TransactionList> HandleAsync(ListTransactions query, CancellationToken cancellationToken = default)
    {
        // One more than the page holds says whether another page follows.
        var found = await transactions.ListAsync(query.Filter, query.Sort, query.Limit + 1, query.After, cancellationToken);
        var next = found.Count > query.Limit ? TransactionCursor.At(found[query.Limit - 1]).ToString() : null;
        var page = new Page<TransactionDetails>([.. found.Take(query.Limit).Select(TransactionDetails.Of)], next);

        var total = Total(await transactions.SumAsync(query.Filter, cancellationToken));
        if (query.Filter.CategoryId is { } categoryId)
        {
            total = total with { Spent = await SpentAsync(query.Filter, categoryId, cancellationToken) };
        }

        return new TransactionList(page, total);
    }

    /// <summary>
    /// Spent by the expenses that pass the filter, added up here by <see cref="Transaction.SpentAmountIn"/>, as
    /// budgets add it up, so that the two never disagree, rounding of a converted split included.
    /// </summary>
    private async Task<SpentTotal> SpentAsync(TransactionFilter filter, Guid categoryId, CancellationToken cancellationToken)
    {
        Guid[] categoryIds = [categoryId, .. (await categories.ListSubcategoriesAsync(categoryId, cancellationToken)).Select(category => category.Id)];
        var expenses = await transactions.ListExpensesAsync(filter, cancellationToken);
        return new SpentTotal(
            await baseCurrency.OfCurrentUserAsync(cancellationToken),
            expenses.Sum(expense => expense.SpentAmountIn(categoryIds)),
            expenses.Count(expense => !expense.IsConverted));
    }

    /// <summary>
    /// The database adds up the amounts of each type in each currency; the signs are applied to those sums here,
    /// by their one definitions: <see cref="TransactionTypes.Signed"/> for the net, <see cref="Spent.Signed"/>
    /// for the expenses. Only income and expenses are added up: a leg of a transfer (spec section 3, rule 3) is
    /// listed and counted, and is neither.
    /// </summary>
    private static FilteredTotal Total(IReadOnlyList<TransactionSum> sums) =>
        new(sums.Sum(sum => sum.Count),
            [.. sums
                .Where(sum => sum.Type.IsIncomeOrExpense())
                .GroupBy(sum => sum.Currency)
                .OrderBy(currency => currency.Key)
                .Select(currency => new CurrencyTotal(
                    currency.Key,
                    currency.Where(sum => sum.Type == TransactionType.Income).Sum(sum => sum.Amount),
                    currency.Sum(sum => Spent.Signed(sum.Type, sum.Amount, sum.IsRefund)),
                    currency.Sum(sum => sum.Type.Signed(sum.Amount, sum.IsRefund))))]);
}

/// <summary>
/// How many transactions of one type in one currency, refunds apart from other expenses, passed a filter, and
/// their amounts added up.
/// </summary>
public sealed record TransactionSum(Currency Currency, TransactionType Type, bool IsRefund, int Count, decimal Amount);

/// <summary>
/// Where a page of transactions ends: the date, amount and id of its last one, opaque to clients. It holds
/// what either sort order is keyed on, so a page never repeats or skips a transaction that was there before,
/// even when others are recorded in between.
/// </summary>
public sealed record TransactionCursor(DateOnly Date, decimal Amount, Guid Id)
{
    private const string DateFormat = "yyyyMMdd";

    private const int AmountAt = 8 + 32;

    public static TransactionCursor At(Transaction transaction) => new(transaction.Date, transaction.Amount, transaction.Id);

    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out TransactionCursor? parsed)
    {
        parsed = null;
        if (cursor is null || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        if (text.Length <= AmountAt
            || !DateOnly.TryParseExact(text[..DateFormat.Length], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !Guid.TryParseExact(text[DateFormat.Length..AmountAt], "N", out var id)
            || !AmountText.TryParse(text[AmountAt..], out var amount))
        {
            return false;
        }

        parsed = new TransactionCursor(date, amount, id);
        return true;
    }

    public override string ToString() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{Date.ToString(DateFormat, CultureInfo.InvariantCulture)}{Id:N}{Amount.ToString(CultureInfo.InvariantCulture)}"));
}
