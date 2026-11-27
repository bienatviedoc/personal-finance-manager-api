using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Transactions.Categorisation;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// What quick add starts a new transaction from (spec TXN-8): the account the user recorded on last, and the
/// category they most often gave <paramref name="Payee"/>.
/// </summary>
/// <param name="Type">The type being recorded: only a category of the kind it takes is suggested. Null for a category of either kind.</param>
/// <param name="Payee">Who is being paid, or is paying, as typed so far; null when nothing is typed yet.</param>
public sealed record GetQuickAdd(TransactionType? Type = null, string? Payee = null);

/// <param name="LastAccountId">
/// The account, not archived, that the user recorded a transaction on last; null when they have recorded none.
/// </param>
/// <param name="SuggestedCategoryId">
/// The category, not archived, most often used on the user's transactions with this payee, and of those used
/// equally often the one used last; for a payee never used, the category of the highest-priority categorisation
/// rule it matches (spec IMP-4); null when neither gives one.
/// </param>
/// <param name="SuggestedBy">What suggested the category; null when none is.</param>
public sealed record QuickAddDetails(Guid? LastAccountId, Guid? SuggestedCategoryId, CategorySuggestion? SuggestedBy = null);

public sealed class GetQuickAddHandler(IQuickAddHistory history, IAccountLookup accounts, UserCategorisation categorisation) : IQueryHandler
{
    public async Task<QuickAddDetails> HandleAsync(GetQuickAdd query, CancellationToken cancellationToken = default)
    {
        var (suggested, by) = Transaction.Normalised(query.Payee) is { } payee ? await SuggestAsync(payee, query.Type, cancellationToken) : (null, null);

        return new QuickAddDetails(await LastAccountAsync(cancellationToken), suggested, by);
    }

    /// <summary>
    /// The category the payee's history suggests, or else the one a categorisation rule gives it. A leg of a transfer
    /// takes no category, so no rule suggests one for it.
    /// </summary>
    private async Task<(Guid?, CategorySuggestion?)> SuggestAsync(string payee, TransactionType? type, CancellationToken cancellationToken)
    {
        if (await history.MostUsedCategoryAsync(payee, type, cancellationToken) is { } used)
        {
            return (used, CategorySuggestion.History);
        }

        if (type?.IsTransferLeg() == true)
        {
            return (null, null);
        }

        return (await categorisation.CategoriserAsync(cancellationToken)).CategoryFor(payee, KindOf(type)) is { } ruled
            ? (ruled, CategorySuggestion.Rule)
            : (null, null);
    }

    private static CategoryKind? KindOf(TransactionType? type) => type switch
    {
        TransactionType.Income => CategoryKind.Income,
        TransactionType.Expense => CategoryKind.Expense,
        _ => null,
    };

    /// <summary>An archived account takes no new transactions, so it is passed over for the one used before it.</summary>
    private async Task<Guid?> LastAccountAsync(CancellationToken cancellationToken)
    {
        foreach (var accountId in await history.AccountsLastUsedFirstAsync(cancellationToken))
        {
            if (await accounts.FindAsync(accountId, cancellationToken) is { IsArchived: false })
            {
                return accountId;
            }
        }

        return null;
    }
}
