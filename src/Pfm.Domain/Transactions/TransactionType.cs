namespace Pfm.Domain.Transactions;

/// <summary>
/// What kind of movement a transaction is. Amounts are stored positive and the type gives the sign
/// (spec section 3, rule 2). No type is zero, so an unset type is never valid. The legs of a transfer
/// are types too, and arrive with transfers.
/// </summary>
public enum TransactionType
{
    Income = 1,
    Expense = 2,
}

public static class TransactionTypes
{
    /// <summary>The kind of category a transaction of this type takes: income for income, expense for an expense.</summary>
    public static CategoryKind CategoryKind(this TransactionType type) => type switch
    {
        TransactionType.Income => Transactions.CategoryKind.Income,
        TransactionType.Expense => Transactions.CategoryKind.Expense,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };

    /// <summary>
    /// The amount as it moves the account's balance: income adds it and an expense takes it away, except a
    /// refund, an expense that adds it (spec section 3, rule 2).
    /// </summary>
    /// <param name="isRefund">Whether the expense is a refund. Only an expense can be one.</param>
    public static decimal Signed(this TransactionType type, decimal amount, bool isRefund = false) => type switch
    {
        TransactionType.Income when isRefund => throw new ArgumentException("Only an expense can be a refund.", nameof(isRefund)),
        TransactionType.Income => amount,
        TransactionType.Expense => isRefund ? amount : -amount,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };
}
