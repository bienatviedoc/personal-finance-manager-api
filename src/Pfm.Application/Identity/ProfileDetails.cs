using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>The signed-in user and their profile settings.</summary>
/// <param name="TimeZone">An IANA time zone id.</param>
/// <param name="Locale">Null until the user picks one; until then the interface follows their browser.</param>
/// <param name="BudgetWarningPercent">The share of a budget, in whole percent, at which its state becomes a warning.</param>
/// <param name="BaseCurrencyIsLocked">
/// Whether the base currency can no longer change, because the user has a budget (spec section 3, rule 12).
/// Where profiles are kept does not know: the use cases ask Budgets and say.
/// </param>
/// <param name="EmailBudgetNotifications">Whether budget notifications are emailed as well as shown in the app.</param>
public sealed record ProfileDetails(
    Guid Id, string Email, Currency BaseCurrency, string TimeZone, Locale? Locale, int BudgetWarningPercent, bool BaseCurrencyIsLocked = false,
    bool EmailBudgetNotifications = true);
