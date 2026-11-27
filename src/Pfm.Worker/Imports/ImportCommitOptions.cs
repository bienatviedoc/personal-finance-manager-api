namespace Pfm.Worker.Imports;

/// <summary>How often the worker looks for committed statements to import (`Imports__CommitEvery`).</summary>
public sealed class ImportCommitOptions
{
    public const string Section = "Imports";

    /// <summary>
    /// How long the worker waits, once nothing is left to import, before it looks again. The user waits up to this
    /// long after committing before the first rows are imported, so it is short; the query behind it reads an index of
    /// the batches being committed, which is empty nearly always.
    /// </summary>
    public TimeSpan CommitEvery { get; set; } = TimeSpan.FromSeconds(2);
}
