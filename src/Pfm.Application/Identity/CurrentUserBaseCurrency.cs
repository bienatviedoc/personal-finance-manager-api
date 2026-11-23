using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>Reads the base currency from the profile settings of the user a job acts for, otherwise of the signed-in user.</summary>
public sealed class CurrentUserBaseCurrency(ICurrentUser currentUser, IActForUser actFor, IUserProfiles profiles) : IBaseCurrency
{
    public async Task<Currency> OfCurrentUserAsync(CancellationToken cancellationToken) =>
        (actFor.UserId ?? currentUser.UserId) is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? profile.BaseCurrency
            : throw new InvalidOperationException("There is no current user to have a base currency.");
}
