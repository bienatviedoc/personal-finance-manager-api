using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>The signed-in user and their profile settings.</summary>
/// <param name="TimeZone">An IANA time zone id.</param>
/// <param name="Locale">Null until the user picks one; until then the interface follows their browser.</param>
public sealed record ProfileDetails(Guid Id, string Email, Currency BaseCurrency, string TimeZone, Locale? Locale);
