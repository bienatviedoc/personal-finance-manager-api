using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;

namespace Pfm.Application.Identity;

/// <summary>Reads the budget warning threshold from the current user's profile settings.</summary>
public sealed class CurrentUserBudgetWarningThreshold(ICurrentUser currentUser, IUserProfiles profiles) : IBudgetWarningThreshold
{
    public async Task<int> OfCurrentUserAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? profile.BudgetWarningPercent
            : throw new InvalidOperationException("There is no current user to have a budget warning threshold.");
}
