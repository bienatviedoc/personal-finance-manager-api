using System.Globalization;
using System.Text.RegularExpressions;

namespace Pfm.Application.UseCases;

/// <summary>
/// Reads an amount as it travels in JSON: a decimal string such as <c>"125000.00"</c>, optionally negative
/// (spec section 11). No exponent, no group separators, no spaces.
/// </summary>
public static partial class AmountText
{
    /// <summary>The amount with at least two decimals and every digit it has: <c>"125000.00"</c>, <c>"-250.75"</c>.</summary>
    public static string Of(decimal amount) => amount.ToString("0.00##", CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out decimal amount)
    {
        amount = default;
        return text is not null && DecimalString().IsMatch(text)
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?$")]
    private static partial Regex DecimalString();
}
