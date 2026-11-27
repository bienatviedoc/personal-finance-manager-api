using Pfm.Domain;

namespace Pfm.Application.Contracts.Identity;

/// <summary>Who a notification is emailed to, and whether they want it emailed.</summary>
public interface INotificationRecipient
{
    /// <summary>The user the request or job acts for; null when it acts for nobody, or their user is gone.</summary>
    Task<NotificationRecipient?> OfCurrentUserAsync(CancellationToken cancellationToken);
}

/// <param name="Locale">Null for a user who has not chosen one: an email cannot follow their browser, so it is in English.</param>
/// <param name="EmailBudgetNotifications">Whether the user wants budget notifications emailed as well as shown in the app.</param>
public sealed record NotificationRecipient(string Email, Locale? Locale, bool EmailBudgetNotifications);
