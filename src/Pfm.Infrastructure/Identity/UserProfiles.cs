using Microsoft.EntityFrameworkCore;
using Pfm.Application.Identity;
using Pfm.Domain.Identity;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Identity;

internal sealed class UserProfiles(PfmDbContext db) : IUserProfiles
{
    public Task<ProfileDetails?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == userId)
            .Select(user => new ProfileDetails(user.Id, user.Email!, user.BaseCurrency, user.TimeZone, user.Locale, user.BudgetWarningPercent))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> ChangeSettingsAsync(Guid userId, ProfileSettings settings, CancellationToken cancellationToken)
    {
        if (await db.Users.FindAsync([userId], cancellationToken) is not { } user)
        {
            return false;
        }

        user.BaseCurrency = settings.BaseCurrency;
        user.TimeZone = settings.TimeZone;
        user.Locale = settings.Locale;
        user.BudgetWarningPercent = settings.BudgetWarningPercent;
        return true;
    }
}
