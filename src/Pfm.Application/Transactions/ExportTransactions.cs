using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Every one of the user's transactions that passes <paramref name="Filter"/>, in the order of <paramref name="Sort"/>,
/// as CSV to download (spec section 7, Export): the whole list, however many pages it would take. Deleted ones are
/// never in it.
/// </summary>
public sealed record ExportTransactions(TransactionFilter Filter, TransactionSort Sort);

/// <param name="FileName">The name to save the download under, with the user's date of the export.</param>
/// <param name="WriteCsvAsync">Writes the CSV file to the stream a part at a time, so a long list is never held whole.</param>
public sealed record TransactionExport(string FileName, Func<Stream, CancellationToken, Task> WriteCsvAsync);

/// <summary>
/// One line for each transaction, under a line of column names: its date as <c>YYYY-MM-DD</c>, its type and
/// whether it is a refund as the API names them, the names of its account and category (a split's categories in
/// the order of its lines, a leg of a transfer none), its tags in the order the list gives them, and its amount as a plain decimal, above zero as the API
/// answers it, beside its currency's code in a column of its own.
/// </summary>
public sealed class ExportTransactionsHandler(
    ITransactionRepository transactions, IAccountLookup accounts, ICategoryRepository categories, IUserCalendar calendar) : IQueryHandler
{
    /// <summary>How many transactions are read from the database at a time.</summary>
    private const int Batch = 500;

    private static readonly string[] Columns = ["date", "type", "isRefund", "account", "category", "payee", "note", "tags", "amount", "currency"];

    public async Task<TransactionExport> HandleAsync(ExportTransactions query, CancellationToken cancellationToken = default)
    {
        var today = await calendar.TodayAsync(cancellationToken);
        return new TransactionExport($"transactions-{DateText.Of(today)}.csv", (target, cancel) => WriteCsvAsync(target, query, cancel));
    }

    private async Task WriteCsvAsync(Stream target, ExportTransactions query, CancellationToken cancellationToken)
    {
        // Archived ones too: the transactions on them still name them.
        var accountNames = (await accounts.ListAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);
        var categoryNames = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id, category => category.Name);

        await using var writer = new StreamWriter(target, Csv.Encoding, leaveOpen: true);
        Csv.WriteColumns(writer, Columns);
        TransactionCursor? after = null;
        while (true)
        {
            var batch = await transactions.ListAsync(query.Filter, query.Sort, Batch, after, cancellationToken);
            Csv.WriteRows(writer, batch.Select(transaction => Row(transaction, accountNames, categoryNames)));
            await writer.FlushAsync(cancellationToken);
            if (batch.Count < Batch)
            {
                return;
            }

            after = TransactionCursor.At(batch[^1]);
        }
    }

    private static object?[] Row(Transaction transaction, Dictionary<Guid, string> accountNames, Dictionary<Guid, string> categoryNames) =>
    [
        transaction.Date,
        EnumText.Of(transaction.Type),
        transaction.IsRefund,
        accountNames.GetValueOrDefault(transaction.AccountId),
        string.Join("; ", transaction.Lines.Select(line => categoryNames.GetValueOrDefault(line.CategoryId))),
        transaction.Payee,
        transaction.Note,
        string.Join("; ", transaction.Tags.Select(tag => tag.Name)),
        new Csv.Number(AmountText.Of(transaction.Amount)),
        transaction.Currency.ToString(),
    ];
}
