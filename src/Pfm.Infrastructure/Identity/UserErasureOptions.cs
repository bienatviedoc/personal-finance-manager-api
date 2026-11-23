namespace Pfm.Infrastructure.Identity;

/// <summary>How often the worker erases deactivated users whose 30 days are over. The default suits every environment.</summary>
public sealed class UserErasureOptions
{
    public const string Section = "UserErasure";

    /// <summary>
    /// How long the worker waits between runs. A user is erased within this long of their 30 days ending; until
    /// then they can no longer sign in, so nothing of theirs is reachable meanwhile.
    /// </summary>
    public TimeSpan RunEvery { get; set; } = TimeSpan.FromHours(1);
}
