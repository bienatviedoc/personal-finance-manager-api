namespace Pfm.Domain.Identity;

/// <summary>
/// How a user wants their money and dates handled: the base currency totals are shown in, the time zone
/// calendar dates are read in, the locale of the interface, and how much of a budget may be spent before
/// they are warned, and whether that warning is emailed too.
/// </summary>
/// <param name="TimeZone">An IANA time zone id, such as <c>Asia/Ho_Chi_Minh</c>.</param>
/// <param name="Locale">Null until the user picks one; until then the interface follows their browser.</param>
/// <param name="BudgetWarningPercent">The share of a budget, in whole percent from 1 to 100, at which its state becomes a warning (spec BUD-3).</param>
/// <param name="EmailBudgetNotifications">Whether budget notifications are emailed as well as shown in the app (spec BUD-7). On until the user switches it off.</param>
public sealed record ProfileSettings(
    Currency BaseCurrency, string TimeZone, Locale? Locale, int BudgetWarningPercent = ProfileSettings.DefaultBudgetWarningPercent, bool EmailBudgetNotifications = true)
{
    public const int DefaultBudgetWarningPercent = 80;

    public int BudgetWarningPercent { get; } = IsValidBudgetWarningPercent(BudgetWarningPercent)
        ? BudgetWarningPercent
        : throw new ArgumentOutOfRangeException(nameof(BudgetWarningPercent), BudgetWarningPercent, "A budget warning threshold is from 1 to 100 percent.");

    public static bool IsValidBudgetWarningPercent(int percent) => percent is >= 1 and <= 100;

    public string TimeZone { get; } = IsValidTimeZone(TimeZone)
        ? TimeZone
        : throw new ArgumentException("A time zone is an IANA time zone id, such as Asia/Ho_Chi_Minh.", nameof(TimeZone));

    /// <summary>An IANA time zone id this server knows. Windows ids and UTC offsets are not.</summary>
    public static bool IsValidTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) && zone.HasIanaId;

    /// <summary>What a new user starts with. Vietnam first: the product is built around Vietnamese banks and wallets.</summary>
    public static readonly ProfileSettings Default = new(Currency.VND, "Asia/Ho_Chi_Minh", null);
}
