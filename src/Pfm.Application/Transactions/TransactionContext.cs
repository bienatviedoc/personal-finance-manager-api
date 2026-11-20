using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Reads what a transaction is recorded against: the account and category the command names, today's date
/// for the user and their base currency. Shared by recording and editing.
/// </summary>
public sealed class TransactionContext(IAccountLookup accounts, ICategoryRepository categories, IUserCalendar calendar, IBaseCurrency baseCurrency)
{
    /// <summary>
    /// What the transaction is recorded against, or the error, keyed by command property, when the account or
    /// category is not one the transaction can be on.
    /// </summary>
    /// <param name="currentAccountId">The account the transaction is already on, which it may stay on after the account is archived.</param>
    internal async Task<Result<RecordedAgainst>> ReadAsync(ITransactionCommand command, Guid? currentAccountId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null || (account.IsArchived && account.Id != currentAccountId))
        {
            return Result<RecordedAgainst>.Invalid(FieldError.On(nameof(ITransactionCommand.AccountId), "Choose one of your accounts that is not archived."));
        }

        var category = await categories.FindAsync(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result<RecordedAgainst>.Invalid(FieldError.On(nameof(ITransactionCommand.CategoryId), "Choose one of your categories that is not archived."));
        }

        return new RecordedAgainst(account, category, await calendar.TodayAsync(cancellationToken), await baseCurrency.OfCurrentUserAsync(cancellationToken));
    }
}

/// <summary>What a transaction is recorded against.</summary>
/// <param name="Account">One of the user's accounts; the transaction takes its currency.</param>
/// <param name="Today">Today's date in the user's time zone.</param>
/// <param name="BaseCurrency">The currency the transaction's amount is converted into.</param>
internal sealed record RecordedAgainst(AccountSummary Account, Category Category, DateOnly Today, Currency BaseCurrency);
