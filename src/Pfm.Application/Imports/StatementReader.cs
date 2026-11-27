using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>A batch's file read with a mapping, with its likely duplicates: what the preview shows and a commit takes.</summary>
/// <param name="Account">The account the statement is imported into.</param>
/// <param name="LikelyDuplicates">The numbers of the rows that are likely duplicates.</param>
public sealed record StatementRead(AccountSummary Account, Statement Statement, IReadOnlySet<int> LikelyDuplicates);

/// <summary>
/// Reads an uploaded batch's file with a mapping, in the currency of its account and on the user's today, and finds
/// its likely duplicates among the transactions the account already has on the statement's dates (spec IMP-3).
/// The preview and the commit read it the same way, so the rows the user ticked are the rows committed.
/// </summary>
public sealed class StatementReader(IAccountLookup accounts, IImportedTransactions transactions, IUserCalendar calendar)
{
    public async Task<StatementRead> ReadAsync(ImportBatch batch, StatementMapping mapping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var account = await accounts.FindAsync(batch.AccountId, cancellationToken)
            ?? throw new InvalidOperationException("An import batch's account is archived, never deleted.");
        var statement = batch.Read(mapping, account.Currency, await calendar.TodayAsync(cancellationToken));
        var entries = statement.Entries.Select(row => row.Entry!).ToList();
        if (entries.Count == 0)
        {
            return new StatementRead(account, statement, new HashSet<int>());
        }

        var existing = await transactions.ListOnAccountAsync(account.Id, entries.Min(entry => entry.Date), entries.Max(entry => entry.Date), cancellationToken);
        var duplicates = LikelyDuplicates.Among(
            statement,
            existing.Select(transaction => new ExistingEntry(
                transaction.Date, transaction.MoneyIn ? StatementEntryType.Income : StatementEntryType.Expense, transaction.Amount, transaction.Payee)));
        return new StatementRead(account, statement, duplicates);
    }
}
