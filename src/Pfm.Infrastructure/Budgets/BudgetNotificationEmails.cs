using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pfm.Application.Abstractions;
using Pfm.Application.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Domain;
using Pfm.Domain.Budgets;

namespace Pfm.Infrastructure.Budgets;

/// <summary>
/// Emails a budget notification in the user's locale, English for one who has none, with a link to the budget
/// month in the SPA. The notification is already saved and shown in the app, so an email that cannot be sent is
/// logged and fails nothing else.
/// </summary>
internal sealed partial class BudgetNotificationEmails(IEmailSender sender, IOptions<AppOptions> app, ILogger<BudgetNotificationEmails> logger)
    : IBudgetNotificationEmails
{
    public async Task SendAsync(NotificationRecipient recipient, BudgetNotificationDetails notification, CancellationToken cancellationToken)
    {
        var (subject, body) = Text(recipient.Locale, notification);
        try
        {
            await sender.SendAsync(new EmailMessage(recipient.Email, subject, body), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotSent(logger, exception, notification.Id);
        }
    }

    private (string Subject, string Body) Text(Locale? locale, BudgetNotificationDetails notification)
    {
        var link = $"{app.Value.PublicUrl}/budgets/{notification.Month}";
        var settings = $"{app.Value.PublicUrl}/settings/profile";
        var (category, percent) = (notification.Category.Name, notification.PercentSpent);
        return locale switch
        {
            Locale.Vi => (
                notification.State == BudgetState.Over
                    ? $"{category} đã vượt ngân sách tháng {notification.Month.FirstDay.Month}/{notification.Month.FirstDay.Year}"
                    : $"{category} đã dùng {percent}% ngân sách tháng {notification.Month.FirstDay.Month}/{notification.Month.FirstDay.Year}",
                $"""
                Bạn đã chi {Amount(notification.Spent, notification.Currency, "vi-VN")} cho {category}, trên ngân sách {Amount(notification.Budgeted, notification.Currency, "vi-VN")} ({percent}%).

                Xem ngân sách của tháng:

                {link}

                Bạn có thể tắt các email này trong phần cài đặt: {settings}
                """),
            Locale.En or null => (
                notification.State == BudgetState.Over
                    ? $"{category} is over budget for {MonthName(notification.Month)}"
                    : $"{category} is at {percent}% of its budget for {MonthName(notification.Month)}",
                $"""
                You have spent {Amount(notification.Spent, notification.Currency, "en-US")} on {category}, against a budget of {Amount(notification.Budgeted, notification.Currency, "en-US")} ({percent}%).

                See the month's budgets:

                {link}

                You can stop these emails in your settings: {settings}
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "No emails in this locale."),
        };
    }

    private static string MonthName(BudgetMonth month) => month.FirstDay.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-US"));

    private static string Amount(decimal amount, Currency currency, string culture) =>
        $"{amount.ToString($"N{currency.DecimalPlaces()}", CultureInfo.GetCultureInfo(culture))} {currency}";

    [LoggerMessage(Level = LogLevel.Error, Message = "The email of budget notification {NotificationId} could not be sent; it is still shown in the app.")]
    private static partial void NotSent(ILogger logger, Exception exception, Guid notificationId);
}
