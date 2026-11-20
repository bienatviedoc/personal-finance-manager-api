using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>Whether the signed-in user signs in with a second factor.</summary>
public sealed record GetTwoFactor;

public sealed class GetTwoFactorHandler(ICurrentUser currentUser, ITwoFactor twoFactor) : IQueryHandler
{
    /// <summary>Null when nobody is signed in (or their user is gone).</summary>
    public async Task<TwoFactorDetails?> HandleAsync(GetTwoFactor query, CancellationToken cancellationToken = default) =>
        currentUser.UserId is { } userId ? await twoFactor.FindAsync(userId) : null;
}
