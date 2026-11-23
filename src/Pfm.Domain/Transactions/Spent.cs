namespace Pfm.Domain.Transactions;

/// <summary>
/// Spent: the total of a category's expenses in a budget month, minus its refunds, in the base currency
/// (spec section 6, Calculation). It is always derived from transactions, never stored, and can be negative:
/// a refund that lands in a month with less spending in its category takes it below zero, and it is shown as
/// it is, never clamped to zero.
/// </summary>
public static class Spent
{
    /// <summary>
    /// Spent in the calendar month, adding up what the transactions dated in it spent in these categories
    /// (<see cref="Transaction.SpentAmountIn"/>): a split transaction adds each line to its own category. A
    /// refund counts in the month of the refund, whenever the purchase was.
    /// </summary>
    /// <param name="categoryIds">The category and its subcategories.</param>
    /// <param name="year">The year of the budget month.</param>
    /// <param name="month">The month, 1 to 12.</param>
    public static decimal Of(IEnumerable<Transaction> transactions, IReadOnlyCollection<Guid> categoryIds, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(categoryIds);
        return transactions
            .Where(transaction => transaction.Date.Year == year && transaction.Date.Month == month)
            .Sum(transaction => transaction.SpentAmountIn(categoryIds));
    }

    /// <summary>
    /// A base currency amount as it moves spent: an expense adds it and a refund takes it away. The one
    /// definition of that sign, for sums the database adds up per type as well.
    /// </summary>
    /// <param name="isRefund">Whether the expense is a refund. Only an expense can be one.</param>
    public static decimal Signed(TransactionType type, decimal baseCurrencyAmount, bool isRefund) => type switch
    {
        TransactionType.Expense => isRefund ? -baseCurrencyAmount : baseCurrencyAmount,
        TransactionType.Income => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };
}
