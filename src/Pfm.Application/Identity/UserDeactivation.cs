using Pfm.Application.Contracts.Identity;

namespace Pfm.Application.Identity;

/// <summary>Reads whether a user is deactivated from when they asked to be deleted.</summary>
public sealed class UserDeactivation(IUsers users) : IUserDeactivation
{
    public async Task<bool> IsDeactivatedAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.FindByIdAsync(userId) is { DeactivatedAt: not null };
}
