using Pfm.Domain;

namespace Pfm.Application.Contracts.Accounts;

/// <summary>What other modules may ask about the balances of the user's accounts over time. Reports asks it for net worth.</summary>
public interface IAccountBalances
{
    /// <summary>
    /// Every account of the user's, archived ones included (spec section 3, rule 10), with its balance at the end of
    /// each of the days, in the order of the days: none on a day before it opened. A balance is never stored; each
    /// is worked out from the account's transactions.
    /// </summary>
    /// <param name="accountId">Only this account; every account when null. One the user does not have answers nothing.</param>
    Task<IReadOnlyList<AccountBalancesOn>> OnAsync(IReadOnlyList<DateOnly> days, Guid? accountId, CancellationToken cancellationToken);
}

/// <summary>One account's balances, in its own currency.</summary>
/// <param name="HoldsAmountOwed">A credit card or loan, whose balance is negative while the user owes anything (rule 9).</param>
/// <param name="Balances">Its balance at the end of each day asked for; null on a day before it opened.</param>
public sealed record AccountBalancesOn(Guid Id, Currency Currency, bool HoldsAmountOwed, IReadOnlyList<decimal?> Balances);
