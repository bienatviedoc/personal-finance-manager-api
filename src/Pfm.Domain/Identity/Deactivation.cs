namespace Pfm.Domain.Identity;

/// <summary>
/// A user who asks to be deleted is deactivated first: for 30 days signing in again cancels the deletion, and
/// after that everything of theirs is erased (spec section 8, Privacy).
/// </summary>
public static class Deactivation
{
    /// <summary>How long a deactivated user can still cancel their deletion by signing in.</summary>
    public static readonly TimeSpan CancellationPeriod = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether a user deactivated at <paramref name="deactivatedAt"/> is past cancelling: they can no longer
    /// sign in, and their data is erased.
    /// </summary>
    public static bool IsDueForErasure(DateTimeOffset deactivatedAt, DateTimeOffset now) => now - deactivatedAt >= CancellationPeriod;
}
