namespace Pfm.Application.Identity;

/// <summary>How codes are written when they arrive: people type them the way their app or printout shows them.</summary>
public static class TwoFactorCodes
{
    /// <summary>An authenticator code without the spaces or hyphens apps group its digits with.</summary>
    public static string NormalizeCode(string? code) =>
        (code ?? "").Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    /// <summary>A recovery code without surrounding spaces.</summary>
    public static string NormalizeRecoveryCode(string? recoveryCode) => (recoveryCode ?? "").Trim();
}
