using System.Globalization;

namespace Pfm.Domain;

/// <summary>
/// The language a user uses the app in: the interface, how numbers, amounts and dates are written, and
/// the names of the default categories they start with. Its tag is the BCP 47 language tag: <c>en</c> or <c>vi</c>.
/// </summary>
public enum Locale
{
    En,
    Vi,
}

public static class Locales
{
    public static string Tag(this Locale locale) => locale switch
    {
        Locale.En => "en",
        Locale.Vi => "vi",
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
    };

    /// <summary>
    /// Writes an amount in a currency the way the locale does, as the SPA does: <c>₫1,234,568</c> in English,
    /// <c>1.234.568 ₫</c> in Vietnamese. The currency's own number of decimals (none for VND), rounding half away
    /// from zero; a negative amount has its sign first.
    /// </summary>
    public static string WriteAmount(this Locale locale, decimal amount, Currency currency)
    {
        var places = currency.DecimalPlaces();
        var rounded = decimal.Round(amount, places, MidpointRounding.AwayFromZero);
        var number = Number(locale, Math.Abs(rounded), places);
        var sign = rounded < 0 ? "-" : "";
        return locale switch
        {
            Locale.En => $"{sign}{Symbol(locale, currency)}{number}",
            // A no-break space keeps the number and the symbol after it on one line.
            Locale.Vi => $"{sign}{number}\u00A0{Symbol(locale, currency)}",
            _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
        };
    }

    /// <summary>
    /// Writes a percentage the way the locale does, to at most one decimal, as a share or a savings rate is sent:
    /// <c>62.5%</c>, <c>62,5%</c>.
    /// </summary>
    public static string WritePercent(this Locale locale, decimal percent)
    {
        var rounded = decimal.Round(percent, 1, MidpointRounding.AwayFromZero);
        var places = rounded == decimal.Truncate(rounded) ? 0 : 1;
        return $"{(rounded < 0 ? "-" : "")}{Number(locale, Math.Abs(rounded), places)}%";
    }

    private static string Number(Locale locale, decimal amount, int places)
    {
        var invariant = amount.ToString(places == 0 ? "#,##0" : "#,##0." + new string('0', places), CultureInfo.InvariantCulture);
        return locale switch
        {
            Locale.En => invariant,
            // Vietnamese groups with a dot and has a decimal comma.
            Locale.Vi => string.Concat(invariant.Select(character => character switch { ',' => '.', '.' => ',', _ => character })),
            _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
        };
    }

    private static string Symbol(Locale locale, Currency currency) => currency switch
    {
        Currency.VND => "₫",
        Currency.USD => locale == Locale.Vi ? "US$" : "$",
        Currency.EUR => "€",
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, null),
    };

    /// <summary>The locale with exactly this tag.</summary>
    public static bool TryParse(string? tag, out Locale locale)
    {
        foreach (var candidate in Enum.GetValues<Locale>())
        {
            if (candidate.Tag() == tag)
            {
                locale = candidate;
                return true;
            }
        }

        locale = default;
        return false;
    }
}
