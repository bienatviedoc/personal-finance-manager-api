using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>Where users' profile settings are kept.</summary>
public interface IUserProfiles
{
    /// <summary>The user's profile, or null when there is no such user.</summary>
    Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the user's settings with new ones. Returns false when there is no such user.</summary>
    Task<bool> SaveAsync(Guid userId, ProfileSettings settings, CancellationToken cancellationToken);
}
