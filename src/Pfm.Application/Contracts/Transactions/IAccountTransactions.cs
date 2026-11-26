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

    /// <summary>Whether the account has any transaction, which fixes its currency.</summary>
    Task<bool> AnyOnAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// What each account's transactions dated before <paramref name="before"/> come to, as
    /// <see cref="NetByAccountAsync(IReadOnlyCollection{Guid}, CancellationToken)"/> adds them up. An account with
    /// none is left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> NetByAccountBeforeAsync(IReadOnlyCollection<Guid> accountIds, DateOnly before, CancellationToken cancellationToken);

    /// <summary>
    /// What each account's transactions dated from <paramref name="from"/> to <paramref name="to"/> come to on
    /// each day, as <see cref="NetByAccountAsync(IReadOnlyCollection{Guid}, CancellationToken)"/> adds them up. A
    /// day an account has no transaction on is left out.
    /// </summary>
    Task<IReadOnlyList<AccountNetOnDay>> NetByDayAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>What one account's transactions came to on one day, in the account's currency.</summary>
public sealed record AccountNetOnDay(Guid AccountId, DateOnly Date, decimal Net);
