using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>Reads the base currency from the current user's profile settings.</summary>
public sealed class CurrentUserBaseCurrency(ICurrentUser currentUser, IUserProfiles profiles) : IBaseCurrency
{
    public async Task<Currency> OfCurrentUserAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? profile.BaseCurrency
            : throw new InvalidOperationException("There is no current user to have a base currency.");
}
