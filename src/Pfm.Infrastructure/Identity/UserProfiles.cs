using Microsoft.EntityFrameworkCore;
using Pfm.Application.Identity;
using Pfm.Domain.Identity;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Identity;

internal sealed class UserProfiles(PfmDbContext db) : IUserProfiles
{
    public Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == userId)
            .Select(user => new UserProfile(user.Id, user.Email!, user.BaseCurrency, user.TimeZone, user.Locale))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> SaveAsync(Guid userId, ProfileSettings settings, CancellationToken cancellationToken)
    {
        var updated = await db.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.BaseCurrency, settings.BaseCurrency)
                    .SetProperty(user => user.TimeZone, settings.TimeZone)
                    .SetProperty(user => user.Locale, settings.Locale),
                cancellationToken);
        return updated == 1;
    }
}
