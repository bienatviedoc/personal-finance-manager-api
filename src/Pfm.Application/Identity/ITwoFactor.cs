namespace Pfm.Application.Identity;

/// <summary>
/// Two-factor sign-in with an authenticator app (TOTP) and recovery codes. Implemented in Infrastructure over
/// ASP.NET Core Identity, which owns the keys, the codes and how they are checked.
/// </summary>
public interface ITwoFactor
{
    /// <summary>Null when there is no such user.</summary>
    Task<TwoFactorDetails?> FindAsync(Guid userId);

    /// <summary>
    /// The key to set an authenticator app up with. A user who has not confirmed one yet keeps the key they were
    /// shown before, so a half-finished set-up can be picked up again.
    /// </summary>
    Task<string> AuthenticatorKeyAsync(Guid userId);

    /// <summary>Whether this is a code the user's authenticator app shows now. False when no key was set up.</summary>
    Task<bool> CheckCodeAsync(Guid userId, string code);

    /// <summary>Turns two-factor sign-in on, ending the user's sessions, and answers their new recovery codes.</summary>
    Task<IReadOnlyList<string>> TurnOnAsync(Guid userId);

    /// <summary>Replaces the user's recovery codes; the old ones stop working.</summary>
    Task<IReadOnlyList<string>> NewRecoveryCodesAsync(Guid userId);

    /// <summary>
    /// Turns two-factor sign-in off, ending the user's sessions, and forgets the key and recovery codes: turning it
    /// on again starts from a new key.
    /// </summary>
    Task TurnOffAsync(Guid userId);

    /// <summary>Uses up one recovery code. False when the user has no such code left.</summary>
    Task<bool> RedeemRecoveryCodeAsync(Guid userId, string recoveryCode);
}

/// <summary>Whether a user signs in with a second factor, and how many recovery codes they have not used yet.</summary>
public sealed record TwoFactorDetails(bool Enabled, int RecoveryCodesLeft);

/// <summary>Recovery codes, shown to the user once: each signs them in one time when they have no authenticator.</summary>
public sealed record RecoveryCodesDetails(IReadOnlyList<string> RecoveryCodes);

/// <summary>How codes are written when they arrive: people type them the way their app or printout shows them.</summary>
public static class TwoFactorCodes
{
    /// <summary>An authenticator code without the spaces or hyphens apps group its digits with.</summary>
    public static string NormalizeCode(string? code) =>
        (code ?? "").Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    /// <summary>A recovery code without surrounding spaces.</summary>
    public static string NormalizeRecoveryCode(string? recoveryCode) => (recoveryCode ?? "").Trim();
}
