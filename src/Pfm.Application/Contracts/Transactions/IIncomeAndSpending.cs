namespace Pfm.Application.Contracts.Transactions;

/// <summary>What other modules may ask about the money the user's transactions brought in and spent, for reports (spec RPT-3).</summary>
public interface IIncomeAndSpending
{
    /// <summary>
    /// Each income and expense dated from <paramref name="from"/> to <paramref name="to"/>, with what it brought in
    /// or spent in the base currency, as converted at the exchange rate of its own date (decision record 0002): a
    /// refund spends a negative amount. Transfers are neither, and are never among them; nor is a deleted
    /// transaction. In no particular order.
    /// </summary>
    /// <param name="accountId">Only those on this account; on every account when null.</param>
    /// <param name="categoryIds">
    /// Only those with a line in one of these categories, each counting only what its lines in them come to (a
    /// split shares its converted amount among its lines); every category when null.
    /// </param>
    Task<IReadOnlyList<MoneyInOrOut>> IncomeAndExpensesAsync(
        DateOnly from, DateOnly to, Guid? accountId, IReadOnlyCollection<Guid>? categoryIds, CancellationToken cancellationToken);
}

/// <summary>One income or expense as it counts in a report.</summary>
/// <param name="Income">What income brought in, in the base currency; zero for an expense.</param>
/// <param name="Spent">What an expense spent, in the base currency, negative for a refund; zero for income.</param>
/// <param name="IsConverted">False while it has no amount in the base currency, when both are zero.</param>
public sealed record MoneyInOrOut(Guid TransactionId, DateOnly Date, decimal Income, decimal Spent, bool IsConverted);
