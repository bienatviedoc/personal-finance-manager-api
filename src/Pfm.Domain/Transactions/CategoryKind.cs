namespace Pfm.Domain.Transactions;

/// <summary>Whether a category is for income or for expenses. No kind is zero, so an unset kind is never valid.</summary>
public enum CategoryKind
{
    Income = 1,
    Expense = 2,
}
