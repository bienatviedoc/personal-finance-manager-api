using Microsoft.AspNetCore.Identity;
using Pfm.Domain;
using Pfm.Domain.Identity;

namespace Pfm.Infrastructure.Identity;

/// <summary>A person who signs in and owns their own separate set of financial data.</summary>
public sealed class User : IdentityUser<Guid>
{
    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Currency BaseCurrency { get; set; } = ProfileSettings.Default.BaseCurrency;

    /// <summary>An IANA time zone id.</summary>
    public string TimeZone { get; set; } = ProfileSettings.Default.TimeZone;

    public Locale? Locale { get; set; } = ProfileSettings.Default.Locale;

    /// <summary>
    /// When the user asked to be deleted; null for a user who has not, or who cancelled by signing in again.
    /// See <see cref="Deactivation"/> for what happens after.
    /// </summary>
    public DateTimeOffset? DeactivatedAt { get; set; }

    /// <summary>The share of a budget, in whole percent, at which its state becomes a warning.</summary>
    public int BudgetWarningPercent { get; set; } = ProfileSettings.Default.BudgetWarningPercent;
}
