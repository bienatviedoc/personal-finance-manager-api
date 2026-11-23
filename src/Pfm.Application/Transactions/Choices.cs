using Pfm.Application.Contracts.Accounts;

namespace Pfm.Application.Transactions;

/// <summary>Which of the user's accounts and categories a transaction, a transfer or a recurring rule may be put on, and what to say when it may not.</summary>
internal static class Choices
{
    public const string NotAnAccount = "Choose one of your accounts that is not archived.";

    public const string NotACategory = "Choose one of your categories that is not archived.";

    /// <summary>
    /// The account, when it is one of the user's that is not archived, or the one already in use (<paramref name="keeps"/>),
    /// which stays usable after it is archived; otherwise null.
    /// </summary>
    public static async Task<AccountSummary?> FindChoosableAsync(this IAccountLookup accounts, Guid accountId, Guid? keeps, CancellationToken cancellationToken) =>
        await accounts.FindAsync(accountId, cancellationToken) is { } account && (!account.IsArchived || account.Id == keeps) ? account : null;
}
