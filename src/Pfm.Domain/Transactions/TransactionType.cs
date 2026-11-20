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

    /// <summary>The amount as it moves the account's balance: income adds it, an expense takes it away.</summary>
    public static decimal Signed(this TransactionType type, decimal amount) => type switch
    {
        TransactionType.Income => amount,
        TransactionType.Expense => -amount,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };
}
