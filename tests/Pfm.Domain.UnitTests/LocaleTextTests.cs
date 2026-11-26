namespace Pfm.Domain.UnitTests;

/// <summary>
/// Amounts and percentages written the locale's way, as the SPA writes them (its formatMoney and formatShare), so
/// a document the API makes reads like the screen.
/// </summary>
public class LocaleTextTests
{
    // A number and its currency symbol after it are kept together by a no-break space.
    private const char Nbsp = '\u00A0';

    [Theory]
    [InlineData(Currency.VND, Locale.En, "₫1,234,568")]
    [InlineData(Currency.VND, Locale.Vi, "1.234.568\u00A0₫")]
    [InlineData(Currency.USD, Locale.En, "$1,234,567.89")]
    [InlineData(Currency.USD, Locale.Vi, "1.234.567,89\u00A0US$")]
    [InlineData(Currency.EUR, Locale.En, "€1,234,567.89")]
    [InlineData(Currency.EUR, Locale.Vi, "1.234.567,89\u00A0€")]
    public void An_amount_is_written_with_the_locales_separators_and_symbol(Currency currency, Locale locale, string expected)
    {
        Assert.Equal(expected, locale.WriteAmount(1234567.89m, currency));
    }

    [Fact]
    public void Each_currency_shows_its_own_decimals_rounding_half_away_from_zero()
    {
        Assert.Equal($"125.000{Nbsp}₫", Locale.Vi.WriteAmount(125000m, Currency.VND));
        Assert.Equal("$12.00", Locale.En.WriteAmount(12m, Currency.USD));
        Assert.Equal($"0,50{Nbsp}€", Locale.Vi.WriteAmount(0.5m, Currency.EUR));
        Assert.Equal("₫3", Locale.En.WriteAmount(2.5m, Currency.VND));
        Assert.Equal("-₫3", Locale.En.WriteAmount(-2.5m, Currency.VND));
        Assert.Equal("₫999", Locale.En.WriteAmount(999m, Currency.VND));
    }

    [Fact]
    public void A_negative_amount_is_written_with_its_sign_first()
    {
        Assert.Equal("-$1,500.00", Locale.En.WriteAmount(-1500m, Currency.USD));
        Assert.Equal($"-1.500{Nbsp}₫", Locale.Vi.WriteAmount(-1500m, Currency.VND));
    }

    [Theory]
    [InlineData(Locale.En, 62.5, "62.5%")]
    [InlineData(Locale.Vi, 62.5, "62,5%")]
    [InlineData(Locale.En, 40.0, "40%")]
    [InlineData(Locale.Vi, -12.3, "-12,3%")]
    [InlineData(Locale.En, 1234.5, "1,234.5%")]
    public void A_percentage_to_one_decimal_is_written_the_locales_way(Locale locale, double percent, string expected)
    {
        Assert.Equal(expected, locale.WritePercent((decimal)percent));
    }
}
