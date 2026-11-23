namespace Pfm.Application.Abstractions;

/// <summary>
/// Lets a flow that runs for nobody act for one user until the returned handle is disposed: registration
/// creating the new user's data, or a background job working through users one at a time. While it acts
/// for them, user-owned data is that user's, exactly as if they were signed in.
/// </summary>
public interface IActForUser
{
    /// <summary>The user the flow acts for now; null while it acts for nobody.</summary>
    Guid? UserId { get; }

    IDisposable ActFor(Guid userId);
}
