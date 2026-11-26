using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;

namespace Pfm.Application.Identity;

/// <summary>Reads the budget warning threshold from the profile settings of the user a job acts for, otherwise of the signed-in user.</summary>
public sealed class CurrentUserBudgetWarningThreshold(ICurrentUser currentUser, IActForUser actFor, IUserProfiles profiles) : IBudgetWarningThreshold
{
    public async Task<int> OfCurrentUserAsync(CancellationToken cancellationToken) =>
        (actFor.UserId ?? currentUser.UserId) is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? profile.BudgetWarningPercent
            : throw new InvalidOperationException("There is no current user to have a budget warning threshold.");
}
