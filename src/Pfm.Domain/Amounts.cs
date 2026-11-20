namespace Pfm.Domain;

/// <summary>
/// An amount of money in a currency, as it can be stored (spec section 3, rule 1): no more decimals than the
/// currency has, and at most <see cref="MaxIntegerDigits"/> digits before them.
/// </summary>
public static class Amounts
{
    /// <summary>What <c>numeric(19,4)</c> holds before the decimal point.</summary>
    public const int MaxIntegerDigits = 15;

    private const decimal Limit = 1_000_000_000_000_000m;

    public static bool IsValid(decimal amount, Currency currency) =>
        Math.Abs(amount) < Limit && decimal.Round(amount, currency.DecimalPlaces()) == amount;
}
