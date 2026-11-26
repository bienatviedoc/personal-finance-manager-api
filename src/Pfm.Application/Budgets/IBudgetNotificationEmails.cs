using Pfm.Application.Contracts.Identity;

namespace Pfm.Application.Budgets;

/// <summary>Emails a budget notification in the recipient's locale. A failure to send is logged and leaves the notification in the app.</summary>
public interface IBudgetNotificationEmails
{
    Task SendAsync(NotificationRecipient recipient, BudgetNotificationDetails notification, CancellationToken cancellationToken);
}
