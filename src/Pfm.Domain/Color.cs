using System.Diagnostics.CodeAnalysis;

namespace Pfm.Domain;

/// <summary>
/// A colour a user picks for something they name, such as an institution or a category. Written
/// <c>#rrggbb</c>; accepted in either case and kept in lower case, so the same colour is always the same value.
/// </summary>
public sealed record Color
{
    private Color(string hex) => Hex = hex;

    /// <summary><c>#rrggbb</c>, in lower case.</summary>
    public string Hex { get; }

    /// <summary>The colour written <c>#rrggbb</c>, in either case.</summary>
    public static bool TryParse(string? hex, [NotNullWhen(true)] out Color? color)
    {
        color = hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(char.IsAsciiHexDigit) ? new Color(hex.ToLowerInvariant()) : null;
        return color is not null;
    }

    public static Color Parse(string hex) =>
        TryParse(hex, out var color) ? color : throw new ArgumentException("A colour is written #rrggbb.", nameof(hex));

    public override string ToString() => Hex;
}
