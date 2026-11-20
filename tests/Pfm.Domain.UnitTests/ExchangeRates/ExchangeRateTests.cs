using Pfm.Domain.ExchangeRates;

namespace Pfm.Domain.UnitTests.ExchangeRates;

public class ExchangeRateTests
{
    private static readonly DateOnly Friday = new(2026, 11, 20);

    [Theory]
    [InlineData(Currency.USD, Currency.VND, "25775", "50.25", "1295194")] // 1295193.75
    [InlineData(Currency.USD, Currency.VND, "25775", "-50.25", "-1295194")]
    [InlineData(Currency.USD, Currency.EUR, "0.91038", "100", "91.04")] // 91.038
    [InlineData(Currency.EUR, Currency.USD, "1.25", "0.10", "0.13")] // 0.125: a half rounds away from zero
    [InlineData(Currency.VND, Currency.USD, "0.00004", "1000000", "40")]
    [InlineData(Currency.USD, Currency.VND, "25775", "0", "0")]
    public void A_rate_converts_an_amount_of_its_base_currency_into_its_quote_currency_rounded_to_that_currencys_decimals(
        Currency from, Currency to, string rate, string amount, string converted)
    {
        var exchangeRate = ExchangeRate.Create(from, to, Friday, decimal.Parse(rate));

        Assert.Equal(decimal.Parse(converted), exchangeRate.Convert(decimal.Parse(amount)));
    }

    [Theory]
    [InlineData(Currency.USD, Currency.VND, "0")]
    [InlineData(Currency.USD, Currency.VND, "-25775")]
    [InlineData(Currency.USD, Currency.USD, "1")]
    [InlineData(Currency.USD, Currency.VND, "10000000000")] // more than numeric(28,18) holds
    public void A_rate_is_a_positive_number_between_two_different_currencies(Currency from, Currency to, string rate)
    {
        Assert.Throws<ArgumentException>(() => ExchangeRate.Create(from, to, Friday, decimal.Parse(rate)));
    }

    [Fact]
    public void One_days_quotes_against_one_currency_give_the_rate_of_every_pair_in_both_directions()
    {
        var quotes = new Dictionary<Currency, decimal> { [Currency.VND] = 25000m, [Currency.EUR] = 0.8m };

        var rates = ExchangeRate.BetweenEvery(Currency.USD, Friday, quotes);

        Assert.Equal(
            [
                ("EUR", "USD", 1.25m),
                ("EUR", "VND", 31250m),
                ("USD", "EUR", 0.8m),
                ("USD", "VND", 25000m),
                ("VND", "EUR", 0.000032m),
                ("VND", "USD", 0.00004m),
            ],
            rates.Select(rate => (rate.BaseCurrency.ToString(), rate.QuoteCurrency.ToString(), rate.Rate)).Order());
        Assert.All(rates, rate => Assert.Equal(Friday, rate.Date));
    }

    [Fact]
    public void A_rate_worked_out_from_two_quotes_keeps_18_decimals()
    {
        var rates = ExchangeRate.BetweenEvery(Currency.USD, Friday, new Dictionary<Currency, decimal> { [Currency.VND] = 25775m });

        var dongToDollars = rates.Single(rate => rate.BaseCurrency == Currency.VND);

        Assert.Equal(0.000038797284190107m, dongToDollars.Rate); // 1 / 25775 = 0.00003879728419010669...
    }

    [Fact]
    public void A_currency_the_quotes_leave_out_or_quote_at_nothing_gets_no_rate()
    {
        var quotes = new Dictionary<Currency, decimal> { [Currency.VND] = 0m, [Currency.USD] = 1.1m };

        var rates = ExchangeRate.BetweenEvery(Currency.EUR, Friday, quotes);

        Assert.Equal(
            [("EUR", "USD"), ("USD", "EUR")],
            rates.Select(rate => (rate.BaseCurrency.ToString(), rate.QuoteCurrency.ToString())).Order());
    }
}
