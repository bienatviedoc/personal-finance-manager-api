namespace Pfm.Worker.Transactions;

/// <summary>How the worker runs the integrity check, from the <c>IntegrityCheck</c> configuration section.</summary>
public sealed class IntegrityCheckOptions
{
    public const string Section = "IntegrityCheck";

    /// <summary>How long after one run the next starts. The first starts with the worker.</summary>
    public TimeSpan RunEvery { get; set; } = TimeSpan.FromDays(1);
}
