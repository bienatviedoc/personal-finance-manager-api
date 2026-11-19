using Pfm.Application.Abstractions;

namespace Pfm.Application.Identity;

/// <summary>Who is signed in, with their profile settings.</summary>
public sealed class GetProfileHandler(ICurrentUser currentUser, IUserProfiles profiles)
{
    /// <summary>The signed-in user's profile, or null when nobody is signed in (or their user is gone).</summary>
    public async Task<UserProfile?> HandleAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId ? await profiles.FindAsync(userId, cancellationToken) : null;
}
