namespace Pfm.Domain.Reports;

/// <summary>One account's balance at a month end, as net worth counts it.</summary>
/// <param name="HoldsAmountOwed">A credit card or loan: a liability (spec section 3, rule 9). Every other account is an asset.</param>
/// <param name="Balance">The balance in the base currency, negative while something is owed; null when it is not converted.</param>
public sealed record NetWorthBalance(bool HoldsAmountOwed, decimal? Balance);

/// <summary>
/// What a user was worth at the end of one day (spec RPT-5): their assets minus their liabilities, in the base
/// currency. Archived accounts count like any other (rule 10).
/// </summary>
/// <param name="Assets">What every account that is not a credit card or loan holds; an overdrawn one takes away from it.</param>
/// <param name="Liabilities">The amount owed on credit cards and loans, positive; a card paid beyond what was owed takes away from it.</param>
/// <param name="NotConverted">How many balances are left out because no exchange rate exists on or before the day (rule 7).</param>
public sealed record NetWorthOn(DateOnly Date, decimal Assets, decimal Liabilities, int NotConverted)
{
    public decimal NetWorth => Assets - Liabilities;

    public static NetWorthOn Of(DateOnly date, IReadOnlyCollection<NetWorthBalance> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);
        var converted = balances.Where(balance => balance.Balance is not null).ToList();
        return new NetWorthOn(
            date,
            converted.Where(balance => !balance.HoldsAmountOwed).Sum(balance => balance.Balance!.Value),
            0m - converted.Where(balance => balance.HoldsAmountOwed).Sum(balance => balance.Balance!.Value),
            balances.Count - converted.Count);
    }
}
