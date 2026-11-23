namespace Pfm.Application.Contracts.Identity;

/// <summary>How Budgets learns the share of a budget at which the user wants a warning (spec BUD-3).</summary>
public interface IBudgetWarningThreshold
{
    /// <summary>The warning threshold, in percent, of the user the request acts for.</summary>
    /// <exception cref="InvalidOperationException">It acts for no user.</exception>
    Task<int> OfCurrentUserAsync(CancellationToken cancellationToken);
}
