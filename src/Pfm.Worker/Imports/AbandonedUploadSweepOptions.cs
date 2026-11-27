namespace Pfm.Worker.Imports;

/// <summary>How the worker removes statements uploaded and never committed, from the <c>AbandonedUploadSweep</c> configuration section.</summary>
public sealed class AbandonedUploadSweepOptions
{
    public const string Section = "AbandonedUploadSweep";

    /// <summary>
    /// How long after it is uploaded a statement that is not committed is kept (`AbandonedUploadSweep__KeepFor`): long
    /// enough to map and preview it in one sitting and come back to it the same day.
    /// </summary>
    public TimeSpan KeepFor { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long after one run the next starts. The first starts with the worker.</summary>
    public TimeSpan RunEvery { get; set; } = TimeSpan.FromHours(1);
}
