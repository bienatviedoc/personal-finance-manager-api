namespace Pfm.Worker.Transactions;

/// <summary>How the worker removes deleted transactions for good, from the <c>DeletedTransactionPurge</c> configuration section.</summary>
public sealed class DeletedTransactionPurgeOptions
{
    public const string Section = "DeletedTransactionPurge";

    /// <summary>How long after one run the next starts. The first starts with the worker.</summary>
    public TimeSpan RunEvery { get; set; } = TimeSpan.FromHours(1);
}
