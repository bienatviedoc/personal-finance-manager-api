using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;

namespace Pfm.Application.Identity;

/// <summary>Reads the email address, locale and email setting of the user a job acts for, otherwise of the signed-in user.</summary>
public sealed class CurrentUserNotificationRecipient(ICurrentUser currentUser, IActForUser actFor, IUserProfiles profiles) : INotificationRecipient
{
    public async Task<NotificationRecipient?> OfCurrentUserAsync(CancellationToken cancellationToken) =>
        (actFor.UserId ?? currentUser.UserId) is { } userId && await profiles.FindAsync(userId, cancellationToken) is { } profile
            ? new NotificationRecipient(profile.Email, profile.Locale, profile.EmailBudgetNotifications)
            : null;
}
