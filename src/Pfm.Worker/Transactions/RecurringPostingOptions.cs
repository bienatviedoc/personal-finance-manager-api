namespace Pfm.Worker.Transactions;

/// <summary>How the worker posts recurring rules, from the <c>RecurringRules</c> configuration section.</summary>
public sealed class RecurringPostingOptions
{
    public const string Section = "RecurringRules";

    /// <summary>
    /// How often the worker looks for occurrences that are due. A due date starts at midnight in its user's
    /// time zone, so this is how long after that an occurrence is posted at the latest.
    /// </summary>
    public TimeSpan PostEvery { get; set; } = TimeSpan.FromMinutes(10);
}
