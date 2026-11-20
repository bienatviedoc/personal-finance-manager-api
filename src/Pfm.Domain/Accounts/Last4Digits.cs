namespace Pfm.Domain.Accounts;

/// <summary>
/// The last 4 digits of an account or card number, which label an account (spec ACC-8). Exactly 4 digits and
/// nothing else, so a full number can never be taken for one.
/// </summary>
public static class Last4Digits
{
    public const int Length = 4;

    public static bool IsValid(string? last4) => last4 is { Length: Length } && last4.All(char.IsAsciiDigit);
}
