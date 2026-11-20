using System.Globalization;

namespace Pfm.Api;

/// <summary>How an amount of money travels in JSON: a decimal string next to its currency, never a number (spec section 11).</summary>
public static class AmountJson
{
    /// <summary>The amount with at least two decimals and every digit it has: <c>"125000.00"</c>, <c>"-250.75"</c>.</summary>
    public static string Text(decimal amount) => amount.ToString("0.00##", CultureInfo.InvariantCulture);
}
