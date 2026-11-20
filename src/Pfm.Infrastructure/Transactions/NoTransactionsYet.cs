using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Transactions.Categories;

namespace Pfm.Infrastructure.Transactions;

/// <summary>
/// No transactions exist yet, so no category is in use and no account has any: every balance is its opening
/// balance. Replaced by queries on transactions when they arrive (#9).
/// </summary>
internal sealed class NoTransactionsYet : ICategoryUsage, IAccountTransactions
{
    private static readonly IReadOnlyDictionary<Guid, decimal> Nothing = new Dictionary<Guid, decimal>();

    public Task<bool> HasTransactionsAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<IReadOnlyDictionary<Guid, decimal>> NetByAccountAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        Task.FromResult(Nothing);

    public Task<bool> AnyOnAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(false);
}
