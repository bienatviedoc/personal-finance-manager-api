using System.Diagnostics.CodeAnalysis;

namespace Pfm.Domain.Accounts;

/// <summary>
/// The last 4 digits of an account or card number, which label an account (spec ACC-8). Exactly 4 digits and
/// nothing else, so a full number can never be taken for one, and is never held.
/// </summary>
public sealed record Last4Digits
{
    public const int Length = 4;

    private Last4Digits(string digits) => Digits = digits;

    public string Digits { get; }

    public static bool IsValid(string? last4) => last4 is { Length: Length } && last4.All(char.IsAsciiDigit);

    public static bool TryParse(string? last4, [NotNullWhen(true)] out Last4Digits? digits)
    {
        digits = IsValid(last4) ? new Last4Digits(last4!) : null;
        return digits is not null;
    }

    public static Last4Digits Parse(string last4) =>
        TryParse(last4, out var digits) ? digits : throw new ArgumentException("The label is the last 4 digits of the number, never more of it.", nameof(last4));

    public override string ToString() => Digits;
}
