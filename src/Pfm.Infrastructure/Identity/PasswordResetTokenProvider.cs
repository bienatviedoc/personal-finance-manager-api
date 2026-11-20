using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pfm.Infrastructure.Identity;

/// <summary>
/// Password reset links last an hour, much shorter than email confirmation links: a reset link hands
/// over the user, so it should not sit usable in an inbox for long.
/// </summary>
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string ProviderName = "PasswordReset";

    public PasswordResetTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = TimeSpan.FromHours(1);
    }
}

/// <summary>
/// Identity's data-protection tokens with their own lifespan. Like every such token, one is bound to
/// the user's security stamp, which a reset changes: so a link works once.
/// </summary>
public sealed class PasswordResetTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<User>> logger)
    : DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);
