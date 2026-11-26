namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// What other modules may ask about the transactions on an account. Accounts computes every balance from
/// it: a balance is never stored (spec section 10).
/// </summary>
public interface IAccountTransactions
{
    /// <summary>
    /// What each account's transactions come to: income − expenses + refunds + transfers in − transfers out
    /// (spec section 3, rule 2). An account with no transactions is left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> NetByAccountAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>
    /// What each account's transactions dated on or before each of the days come to, in the order of the days,
    /// by the same definition as <see cref="NetByAccountAsync"/>. An account with no transactions is left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<decimal>>> NetByAccountOnAsync(
        IReadOnlyCollection<Guid> accountIds, IReadOnlyList<DateOnly> days, CancellationToken cancellationToken);

    /// <summary>Whether the account has any transaction, which fixes its currency.</summary>
    Task<bool> AnyOnAsync(Guid accountId, CancellationToken cancellationToken);
}
