using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Who is signed in, with their profile settings.</summary>
public sealed record GetProfile;

public sealed class GetProfileHandler(ICurrentUser currentUser, IUserProfiles profiles, IUserBudgets budgets) : IQueryHandler
{
    /// <summary>The signed-in user's profile, or null when nobody is signed in (or their user is gone).</summary>
    public async Task<ProfileDetails?> HandleAsync(GetProfile query, CancellationToken cancellationToken = default) =>
        currentUser.UserId is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? profile with { BaseCurrencyIsLocked = await budgets.AnyAsync(cancellationToken) }
            : null;
}
