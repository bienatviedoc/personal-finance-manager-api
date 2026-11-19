namespace Pfm.Application.Identity;

/// <summary>
/// The signed-in session of whoever made the current request. Implemented by the host that signs
/// people in (the API, with a cookie); hosts that do not get <see cref="NoSessions"/>.
/// </summary>
public interface ISessions
{
    Task SignInAsync(Guid userId);

    Task SignOutAsync();
}

/// <summary>For hosts that sign no one in, such as the worker: starting or ending a session is a mistake there.</summary>
public sealed class NoSessions : ISessions
{
    public Task SignInAsync(Guid userId) => throw new InvalidOperationException("This host does not sign people in.");

    public Task SignOutAsync() => throw new InvalidOperationException("This host does not sign people in.");
}
