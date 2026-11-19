namespace Pfm.Domain.Identity;

/// <summary>
/// How a user wants their money and dates handled: the base currency totals are shown in, the time zone
/// calendar dates are read in, and the locale of the interface.
/// </summary>
/// <param name="TimeZone">An IANA time zone id, such as <c>Asia/Ho_Chi_Minh</c>.</param>
/// <param name="Locale">Null until the user picks one; until then the interface follows their browser.</param>
public sealed record ProfileSettings(Currency BaseCurrency, string TimeZone, Locale? Locale)
{
    /// <summary>What a new user starts with. Vietnam first: the product is built around Vietnamese banks and wallets.</summary>
    public static readonly ProfileSettings Default = new(Currency.VND, "Asia/Ho_Chi_Minh", null);
}
