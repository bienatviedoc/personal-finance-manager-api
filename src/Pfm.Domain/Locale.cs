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
    private static readonly LocaleFormat English = new("en", GroupSeparator: ',', DecimalSeparator: '.', SymbolFirst: true, DollarSymbol: "$");

    private static readonly LocaleFormat Vietnamese = new("vi", GroupSeparator: '.', DecimalSeparator: ',', SymbolFirst: false, DollarSymbol: "US$");

    public static string Tag(this Locale locale) => locale.Format().Tag;

    /// <summary>
    /// Writes an amount in a currency the way the locale does, as the SPA does: <c>₫1,234,568</c> in English,
    /// <c>1.234.568 ₫</c> in Vietnamese. The currency's own number of decimals (none for VND), rounding half away
    /// from zero; a negative amount has its sign first.
    /// </summary>
    public static string WriteAmount(this Locale locale, decimal amount, Currency currency)
    {
        var format = locale.Format();
        var places = currency.DecimalPlaces();
        var rounded = decimal.Round(amount, places, MidpointRounding.AwayFromZero);
        var number = format.Number(Math.Abs(rounded), places);
        var sign = rounded < 0 ? "-" : "";
        var symbol = format.Symbol(currency);
        // A no-break space keeps the number and a symbol after it on one line.
        return format.SymbolFirst ? $"{sign}{symbol}{number}" : $"{sign}{number}\u00A0{symbol}";
    }

    /// <summary>
    /// Writes a percentage the way the locale does, to at most one decimal, as a share or a savings rate is sent:
    /// <c>62.5%</c>, <c>62,5%</c>.
    /// </summary>
    public static string WritePercent(this Locale locale, decimal percent)
    {
        var rounded = decimal.Round(percent, 1, MidpointRounding.AwayFromZero);
        var places = rounded == decimal.Truncate(rounded) ? 0 : 1;
        return $"{(rounded < 0 ? "-" : "")}{locale.Format().Number(Math.Abs(rounded), places)}%";
    }

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

    private static LocaleFormat Format(this Locale locale) => locale switch
    {
        Locale.En => English,
        Locale.Vi => Vietnamese,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
    };

    /// <summary>How one locale writes numbers and amounts.</summary>
    /// <param name="SymbolFirst">Whether a currency's symbol comes before the number, or after it.</param>
    /// <param name="DollarSymbol">What the US dollar is written as; other currencies are written alike everywhere.</param>
    private sealed record LocaleFormat(string Tag, char GroupSeparator, char DecimalSeparator, bool SymbolFirst, string DollarSymbol)
    {
        public string Number(decimal amount, int places)
        {
            var invariant = amount.ToString(places == 0 ? "#,##0" : "#,##0." + new string('0', places), CultureInfo.InvariantCulture);
            return string.Concat(invariant.Select(character => character switch { ',' => GroupSeparator, '.' => DecimalSeparator, _ => character }));
        }

        public string Symbol(Currency currency) => currency switch
        {
            Currency.VND => "₫",
            Currency.USD => DollarSymbol,
            Currency.EUR => "€",
            _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, null),
        };
    }
}
