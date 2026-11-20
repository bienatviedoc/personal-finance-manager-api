using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>Reads the clock in the signed-in user's time zone; in the default one when nobody is signed in.</summary>
public sealed class UserCalendar(ICurrentUser currentUser, IUserProfiles profiles, TimeProvider clock) : IUserCalendar
{
    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var profile = currentUser.UserId is { } userId ? await profiles.FindAsync(userId, cancellationToken) : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(profile?.TimeZone ?? ProfileSettings.Default.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}
