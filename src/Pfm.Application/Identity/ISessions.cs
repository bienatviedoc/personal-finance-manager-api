namespace Pfm.Application.Identity;

/// <summary>
/// The signed-in session of whoever made the current request. Implemented in Infrastructure as a
/// cookie, for a host that signs people in (the API); hosts that do not get <see cref="NoSessions"/>.
/// </summary>
public interface ISessions
{
    Task SignInAsync(Guid userId);

    Task SignOutAsync();

    /// <summary>
    /// Keeps the current request's session going after a change that ends the user's sessions (a new password,
    /// two-factor turned on or off): the others end, this one carries on.
    /// </summary>
    Task RefreshAsync(Guid userId);
}

/// <summary>For hosts that sign no one in, such as the worker: starting or ending a session is a mistake there.</summary>
public sealed class NoSessions : ISessions
{
    public Task SignInAsync(Guid userId) => throw new InvalidOperationException("This host does not sign people in.");

    public Task SignOutAsync() => throw new InvalidOperationException("This host does not sign people in.");

    public Task RefreshAsync(Guid userId) => throw new InvalidOperationException("This host does not sign people in.");
}
