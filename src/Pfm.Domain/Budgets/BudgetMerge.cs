namespace Pfm.Domain.Budgets;

/// <summary>
/// What merging one category into another does to their budgets (spec CAT-3): every budget of the merged
/// category moves to the category it is merged into, and where both have a budget in the same budget month
/// the amounts are added. The merge is refused when it would give the category merged into a budget in a month
/// its parent or one of its subcategories has one too (spec section 3, rule 6).
/// </summary>
public sealed class BudgetMerge
{
    private readonly Guid _intoId;
    private readonly IReadOnlyList<(Budget Merged, Budget? Into)> _moves;

    private BudgetMerge(Guid intoId, IReadOnlyList<(Budget Merged, Budget? Into)> moves)
    {
        _intoId = intoId;
        _moves = moves;
    }

    /// <summary>How many budgets of the merged category move.</summary>
    public int Moving => _moves.Count;

    /// <summary>Checks that the budgets can be merged, changing nothing yet.</summary>
    /// <param name="intoId">The category merged into.</param>
    /// <param name="merged">Every budget of the category being merged.</param>
    /// <param name="into">Every budget of the category merged into.</param>
    /// <param name="relatives">
    /// Every budget of the category merged into's parent and of its subcategories as they are once merged, which
    /// takes in the merged category's own subcategories.
    /// </param>
    public static BudgetMerge Plan(Guid intoId, IReadOnlyCollection<Budget> merged, IReadOnlyCollection<Budget> into, IReadOnlyCollection<Budget> relatives)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(relatives);
        if (merged.Any(budget => budget.CategoryId == intoId) || into.Any(budget => budget.CategoryId != intoId))
        {
            throw new ArgumentException("The budgets of the category merged and of the one merged into are given apart.", nameof(into));
        }

        var relativeMonths = relatives.Select(budget => budget.Month).ToHashSet();
        var clash = merged.Concat(into).Select(budget => budget.Month).Where(relativeMonths.Contains).Order().Cast<BudgetMonth?>().FirstOrDefault();
        if (clash is not null)
        {
            throw new BudgetRuleViolation(
                nameof(Budget.CategoryId),
                $"In {clash} the category merged into would have a budget, and so would its parent category or one of its subcategories. Remove one of them first.");
        }

        var intoByMonth = into.ToDictionary(budget => budget.Month);
        List<(Budget Merged, Budget? Into)> moves = [];
        foreach (var budget in merged)
        {
            var already = intoByMonth.GetValueOrDefault(budget.Month);
            if (already is not null && !Amounts.IsValid(already.Amount + budget.Amount, already.Currency))
            {
                throw new BudgetRuleViolation(
                    nameof(Budget.Amount),
                    $"In {budget.Month} the two budgets add up to more than {Amounts.MaxIntegerDigits} digits, which a budget cannot hold.");
            }

            moves.Add((budget, already));
        }

        return new BudgetMerge(intoId, moves);
    }

    /// <summary>Moves the budgets, adding amounts where both are budgeted in the month.</summary>
    /// <returns>The merged category's budgets whose amounts were added to another, to be removed.</returns>
    public IReadOnlyList<Budget> Apply(DateTimeOffset now)
    {
        List<Budget> removed = [];
        foreach (var (merged, into) in _moves)
        {
            if (into is null)
            {
                merged.MoveTo(_intoId, now);
                continue;
            }

            into.ChangeAmount(into.Amount + merged.Amount, now);
            removed.Add(merged);
        }

        return removed;
    }
}
