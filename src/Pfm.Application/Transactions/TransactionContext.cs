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
        if (await accounts.FindChoosableAsync(command.AccountId, currentAccountId, cancellationToken) is not { } account)
        {
            return Result<RecordedAgainst>.Invalid(FieldError.On(nameof(ITransactionCommand.AccountId), Choices.NotAnAccount));
        }

        List<SplitLine>? split = null;
        if (command.Lines is { } lines)
        {
            split = [];
            foreach (var line in lines)
            {
                if (await categories.FindAsync(line.CategoryId, cancellationToken) is not { } itsCategory)
                {
                    return Result<RecordedAgainst>.Invalid(FieldError.On(
                        $"{nameof(ITransactionCommand.Lines)}[{split.Count}].{nameof(TransactionLineInput.CategoryId)}", Choices.NotACategory));
                }

                split.Add(new SplitLine(itsCategory, line.ParsedAmount!.Value, line.Note));
            }
        }

        var category = split is null ? await categories.FindAsync(command.CategoryId, cancellationToken) : split[0].Category;
        if (category is null)
        {
            return Result<RecordedAgainst>.Invalid(FieldError.On(nameof(ITransactionCommand.CategoryId), Choices.NotACategory));
        }

        return new RecordedAgainst(
            account, category, await calendar.TodayAsync(cancellationToken), await baseCurrency.OfCurrentUserAsync(cancellationToken), split);
    }
}

/// <summary>What a transaction is recorded against.</summary>
/// <param name="Account">One of the user's accounts; the transaction takes its currency.</param>
/// <param name="Today">Today's date in the user's time zone.</param>
/// <param name="Category">The transaction's category; of a split, its first line's.</param>
/// <param name="BaseCurrency">The currency the transaction's amount is converted into.</param>
/// <param name="Split">The lines of a split, each with one of the user's categories; null for a transaction that is not split.</param>
internal sealed record RecordedAgainst(
    AccountSummary Account, Category Category, DateOnly Today, Currency BaseCurrency, IReadOnlyList<SplitLine>? Split = null);
