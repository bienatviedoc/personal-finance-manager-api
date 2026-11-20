using System.Net;
using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

/// <summary>The accounts list's totals in the user's base currency (spec ACC-4), at the rates the fake source quotes.</summary>
[Collection(ApiCollection.Name)]
public class BaseCurrencyTotalTests(PfmApi api) : IAsyncLifetime
{
    private const string Accounts = "/api/v1/accounts";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_list_totals_each_institution_and_everything_in_the_base_currency_at_todays_rates()
    {
        await RatesAreAsync(Today, dong: 25000m, euros: 0.8m);
        var (browser, _) = await api.SignedInAsync();
        var vietcombank = await AddInstitutionAsync(browser, "Vietcombank");
        var wise = await AddInstitutionAsync(browser, "Wise");
        await AddAsync(browser, new { name = "Everyday", type = "current", institutionId = vietcombank, currency = "VND", openingBalance = "1000000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Dollars", type = "savings", institutionId = vietcombank, currency = "USD", openingBalance = "50.25", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Euros", type = "current", institutionId = wise, currency = "EUR", openingBalance = "10", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Euro card", type = "creditCard", institutionId = wise, currency = "EUR", amountOwed = "2", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Pocket", type = "cash", currency = "VND", openingBalance = "5000", openingDate = "2026-11-01" });

        var total = await TotalAsync(browser);

        // 50.25 USD is 1,256,250 VND; 8 EUR is 250,000 VND.
        Assert.Equal(("VND", "2511250.00"), (total.Currency, total.Balance));
        Assert.Empty(total.NotConverted);
        Assert.Equal(
            [new InstitutionBody(null, "5000.00", []), new InstitutionBody(vietcombank, "2256250.00", []), new InstitutionBody(wise, "250000.00", [])],
            total.Institutions.OrderBy(institution => institution.InstitutionId == wise).ThenBy(institution => institution.InstitutionId == vietcombank),
            InstitutionBody.Same);
    }

    [Fact]
    public async Task A_balance_with_no_rate_is_left_out_of_the_totals_and_listed_as_not_converted()
    {
        await RatesAreAsync(Today, dong: 25000m); // the source quotes no euro
        var (browser, _) = await api.SignedInAsync();
        var wise = await AddInstitutionAsync(browser, "Wise");
        await AddAsync(browser, new { name = "Dollars", type = "current", institutionId = wise, currency = "USD", openingBalance = "100", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Euros", type = "current", institutionId = wise, currency = "EUR", openingBalance = "10", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Euro jar", type = "cash", currency = "EUR", openingBalance = "2.50", openingDate = "2026-11-01" });

        var total = await TotalAsync(browser);

        Assert.Equal(("VND", "2500000.00"), (total.Currency, total.Balance));
        Assert.Equal([new AmountBody("EUR", "12.50")], total.NotConverted);
        Assert.Equal(
            [new InstitutionBody(null, "0.00", [new AmountBody("EUR", "2.50")]), new InstitutionBody(wise, "2500000.00", [new AmountBody("EUR", "10.00")])],
            total.Institutions.OrderBy(institution => institution.InstitutionId == wise),
            InstitutionBody.Same);
    }

    [Fact]
    public async Task Before_any_rate_is_fetched_only_balances_already_in_the_base_currency_are_totalled()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, new { name = "Pocket", type = "cash", currency = "VND", openingBalance = "5000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Dollars", type = "cash", currency = "USD", openingBalance = "20", openingDate = "2026-11-01" });

        var total = await TotalAsync(browser);

        Assert.Equal("5000.00", total.Balance);
        Assert.Equal([new AmountBody("USD", "20.00")], total.NotConverted);
    }

    [Fact]
    public async Task The_totals_follow_the_users_base_currency()
    {
        await RatesAreAsync(Today, dong: 25000m, euros: 0.8m);
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, new { name = "Everyday", type = "current", currency = "VND", openingBalance = "1000000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Euros", type = "current", currency = "EUR", openingBalance = "10", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Dollars", type = "current", currency = "USD", openingBalance = "1.10", openingDate = "2026-11-01" });

        (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" })).EnsureSuccessStatusCode();
        var total = await TotalAsync(browser);

        // 1,000,000 VND is 40 USD; 10 EUR is 12.50 USD.
        Assert.Equal(("USD", "53.60"), (total.Currency, total.Balance));
    }

    [Fact]
    public async Task With_no_rate_today_the_totals_use_the_nearest_earlier_one()
    {
        await RatesAreAsync(Today.AddDays(-4), dong: 24000m);
        await RatesAreAsync(Today.AddDays(-2), dong: 25000m);
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, new { name = "Dollars", type = "current", currency = "USD", openingBalance = "2", openingDate = "2026-11-01" });

        var total = await TotalAsync(browser);

        Assert.Equal("50000.00", total.Balance);
        Assert.Empty(total.NotConverted);
    }

    [Fact]
    public async Task Every_user_converts_at_the_same_rates_and_every_page_carries_the_total_of_the_whole_list()
    {
        await RatesAreAsync(Today, dong: 25000m);
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        await AddAsync(alice, new { name = "Dollars", type = "current", currency = "USD", openingBalance = "1", openingDate = "2026-11-01" });
        await AddAsync(alice, new { name = "More dollars", type = "current", currency = "USD", openingBalance = "2", openingDate = "2026-11-01" });
        await AddAsync(bob, new { name = "Dollars", type = "current", currency = "USD", openingBalance = "10", openingDate = "2026-11-01" });

        Assert.Equal("75000.00", (await TotalAsync(alice, "?limit=1")).Balance);
        Assert.Equal("250000.00", (await TotalAsync(bob)).Balance);
    }

    [Fact]
    public async Task A_user_with_no_accounts_has_a_total_of_nothing_in_their_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();

        var total = await TotalAsync(browser);

        Assert.Equal(("VND", "0.00"), (total.Currency, total.Balance));
        Assert.Empty(total.NotConverted);
        Assert.Empty(total.Institutions);
    }

    [Fact]
    public async Task Nobody_can_enter_an_exchange_rate_the_API_has_no_endpoint_for_them()
    {
        var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name);

        Assert.DoesNotContain(paths, path => path.Contains("rate", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<TotalBody> TotalAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Accounts + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountList>())!.BaseCurrencyTotal;
    }

    private async Task RatesAreAsync(DateOnly date, decimal? dong = null, decimal? euros = null)
    {
        api.ExchangeRateSource.Quote(date, dong, euros);
        await api.FetchExchangeRatesAsync();
    }

    private static async Task<Guid> AddInstitutionAsync(Browser browser, string name)
    {
        var response = await browser.PostAsync("/api/v1/institutions", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private static async Task<Guid> AddAsync(Browser browser, object account)
    {
        var response = await browser.PostAsync(Accounts, account);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private sealed record Identified(Guid Id);

    private sealed record AccountList(TotalBody BaseCurrencyTotal);

    private sealed record TotalBody(string Currency, string Balance, AmountBody[] NotConverted, InstitutionBody[] Institutions);

    private sealed record AmountBody(string Currency, string Balance);

    private sealed record InstitutionBody(Guid? InstitutionId, string Balance, AmountBody[] NotConverted)
    {
        /// <summary>Records compare arrays by reference; this compares what they hold.</summary>
        public static bool Same(InstitutionBody one, InstitutionBody other) =>
            (one.InstitutionId, one.Balance) == (other.InstitutionId, other.Balance) && one.NotConverted.SequenceEqual(other.NotConverted);
    }
}
