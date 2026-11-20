namespace Pfm.Domain;

/// <summary>
/// A currency money can be held in, named by its ISO 4217 code. These are the currencies v1 supports
/// (spec section 2: VND, USD and EUR first); adding one means the exchange-rate source must cover it.
/// </summary>
public enum Currency
{
    VND,
    USD,
    EUR,
}

public static class Currencies
{
    /// <summary>How many decimals an amount in the currency has: none for VND, two for the others.</summary>
    public static int DecimalPlaces(this Currency currency) => currency == Currency.VND ? 0 : 2;

    /// <summary>
    /// The currency with exactly this ISO 4217 code. Codes are upper case; numbers and other spellings
    /// are not codes.
    /// </summary>
    public static bool TryParse(string? code, out Currency currency)
    {
        currency = default;
        return code is not null && Enum.GetNames<Currency>().Contains(code) && Enum.TryParse(code, out currency);
    }
}
