using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>
/// Reads the clock in the time zone of the user a job acts for, otherwise of the signed-in user; in the default
/// one when there is neither.
/// </summary>
public sealed class UserCalendar(ICurrentUser currentUser, IActForUser actFor, IUserProfiles profiles, TimeProvider clock) : IUserCalendar
{
    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var profile = (actFor.UserId ?? currentUser.UserId) is { } userId ? await profiles.FindAsync(userId, cancellationToken) : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(profile?.TimeZone ?? ProfileSettings.Default.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}
