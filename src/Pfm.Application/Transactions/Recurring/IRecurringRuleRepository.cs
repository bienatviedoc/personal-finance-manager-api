using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>The signed-in (or acted-for) user's recurring rules. Changes are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IRecurringRuleRepository
{
    /// <summary>Up to <paramref name="take"/> rules, the last set up first, starting after the rule <paramref name="after"/>.</summary>
    Task<IReadOnlyList<RecurringRule>> ListAsync(int take, Guid? after, CancellationToken cancellationToken);

    /// <summary>The user's rule with this id; null when they have none.</summary>
    Task<RecurringRule?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The ids of the user's rules that post, are not paused, and have an occurrence due on or before <paramref name="today"/>.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>
    /// Every user, signed in or not, who has a rule that posts and is not paused with an occurrence due on or before
    /// <paramref name="date"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> UsersWithDueRulesAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The user's rules that are not paused and have an occurrence not settled due on or before <paramref name="date"/>.</summary>
    Task<IReadOnlyList<RecurringRule>> ListUnsettledThroughAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Every rule of the user on the category, paused or not.</summary>
    Task<IReadOnlyList<RecurringRule>> ListOnCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    void Add(RecurringRule rule);

    /// <summary>Deletes the rule. The transactions it posted stay, no longer linked to a rule.</summary>
    void Remove(RecurringRule rule);
}
