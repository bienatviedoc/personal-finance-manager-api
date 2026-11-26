namespace Pfm.Application.Contracts.Budgets;

/// <summary>
/// How a module that changes what was spent has Budgets check whether a category moved into warning or over
/// (spec BUD-7). Call it after saving any change that affects spent: a transaction recorded, edited, deleted,
/// converted or imported.
/// </summary>
public interface IBudgetNotifications
{
    /// <summary>
    /// Raises the budget notifications now due in the budget months of <paramref name="dates"/>, in the app and by
    /// email, for the user the request or job acts for. Each category raises at most one warning and one over a month.
    /// </summary>
    /// <param name="dates">The dates of what changed, before and after the change.</param>
    Task CheckAsync(IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken);
}
