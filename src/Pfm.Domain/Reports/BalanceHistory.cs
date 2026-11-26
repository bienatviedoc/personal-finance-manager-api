namespace Pfm.Domain.Reports;

/// <summary>How often a running balance is shown over a report period (spec RPT-4).</summary>
public enum BalanceInterval
{
    /// <summary>The balance at the end of every day.</summary>
    Day = 1,

    /// <summary>The balance at the end of every week, Monday to Sunday, and at the end of the period.</summary>
    Week = 2,
}

/// <summary>
/// What one account's balance was made of, for its running balance over a report period (spec RPT-4): what it
/// opened with, and what its transactions came to before the period and on each day of it. A balance is never
/// stored; it is worked out from these each time.
/// </summary>
/// <param name="OpeningBalance">What the account held on its opening date, in its own currency; negative for an amount owed.</param>
/// <param name="OpeningDate">From this day on, the opening balance is part of the balance; before it, the account held only what its transactions came to.</param>
/// <param name="NetBefore">What its transactions dated before the period came to.</param>
/// <param name="NetOn">What its transactions came to on each day of the period that has any.</param>
public sealed record BalanceHistory(decimal OpeningBalance, DateOnly OpeningDate, decimal NetBefore, IReadOnlyDictionary<DateOnly, decimal> NetOn)
{
    /// <summary>
    /// The balance at the end of each day, in the order given: the opening balance once the account has opened, plus
    /// every transaction dated on or before the day.
    /// </summary>
    /// <param name="days">Days of the period, earliest first.</param>
    public IReadOnlyList<decimal> BalancesOn(IReadOnlyList<DateOnly> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        var nets = NetOn.OrderBy(net => net.Key).ToList();
        var next = 0;
        var net = NetBefore;
        var balances = new List<decimal>(days.Count);
        foreach (var day in days)
        {
            while (next < nets.Count && nets[next].Key <= day)
            {
                net += nets[next++].Value;
            }

            balances.Add((day >= OpeningDate ? OpeningBalance : 0m) + net);
        }

        return balances;
    }
}
