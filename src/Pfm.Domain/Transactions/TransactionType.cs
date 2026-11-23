namespace Pfm.Domain.Transactions;

/// <summary>
/// What kind of movement a transaction is. Amounts are stored positive and the type gives the sign
/// (spec section 3, rule 2). No type is zero, so an unset type is never valid. The two legs of a transfer
/// are types too (decision record 0001): they are recorded only as a <see cref="Transfer"/>, never on their own.
/// </summary>
public enum TransactionType
{
    Income = 1,
    Expense = 2,
    TransferOut = 3,
    TransferIn = 4,
}

public static class TransactionTypes
{
    /// <summary>Whether a transaction of this type is a leg of a transfer, which is neither income nor spending.</summary>
    public static bool IsTransferLeg(this TransactionType type) => type is TransactionType.TransferOut or TransactionType.TransferIn;

    /// <summary>The kind of category a transaction of this type takes: income for income, expense for an expense. A transfer leg takes none.</summary>
    public static CategoryKind CategoryKind(this TransactionType type) => type switch
    {
        TransactionType.Income => Transactions.CategoryKind.Income,
        TransactionType.Expense => Transactions.CategoryKind.Expense,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };

    /// <summary>
    /// The amount as it moves the account's balance: income and a transfer in add it, an expense and a transfer
    /// out take it away.
    /// </summary>
    public static decimal Signed(this TransactionType type, decimal amount) => type switch
    {
        TransactionType.Income or TransactionType.TransferIn => amount,
        TransactionType.Expense or TransactionType.TransferOut => -amount,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type."),
    };
}
