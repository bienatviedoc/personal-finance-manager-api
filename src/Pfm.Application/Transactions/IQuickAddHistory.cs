using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>What the signed-in user's transactions say about how they record one. A deleted transaction says nothing.</summary>
public interface IQuickAddHistory
{
    /// <summary>The accounts the user has transactions on, the one recorded on last first. A user has dozens, not thousands.</summary>
    Task<IReadOnlyList<Guid>> AccountsLastUsedFirstAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The category, not archived, on the most of the user's transactions with this payee, whatever its case;
    /// of those on equally many, the one used last. Null when no transaction has the payee.
    /// </summary>
    /// <param name="payee">A whole payee, trimmed, never part of one.</param>
    /// <param name="type">Counts only transactions of this type; null counts every one.</param>
    Task<Guid?> MostUsedCategoryAsync(string payee, TransactionType? type, CancellationToken cancellationToken);
}
