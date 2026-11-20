namespace Pfm.Domain.UnitTests;

public class CurrencyTests
{
    [Theory]
    [InlineData("VND", Currency.VND)]
    [InlineData("USD", Currency.USD)]
    [InlineData("EUR", Currency.EUR)]
    public void A_supported_currency_is_found_by_its_ISO_4217_code(string code, Currency expected)
    {
        Assert.True(Currencies.TryParse(code, out var currency));
        Assert.Equal(expected, currency);
    }

    [Theory]
    [InlineData("vnd")]
    [InlineData("JPY")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData(" VND")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_currency(string? code)
    {
        Assert.False(Currencies.TryParse(code, out _));
    }
}
