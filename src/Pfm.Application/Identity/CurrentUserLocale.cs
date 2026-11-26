using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>Reads the locale from the profile settings of the user a job acts for, otherwise of the signed-in user.</summary>
public sealed class CurrentUserLocale(ICurrentUser currentUser, IActForUser actFor, IUserProfiles profiles) : IUserLocale
{
    public async Task<Locale?> OfCurrentUserAsync(CancellationToken cancellationToken) =>
        (actFor.UserId ?? currentUser.UserId) is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? profile.Locale
            : throw new InvalidOperationException("There is no current user to have a locale.");
}
