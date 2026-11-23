using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Finding transactions: the filters, search, sorting and totals of <c>GET /transactions</c> (spec TXN-2, TXN-3).</summary>
[Collection(ApiCollection.Name)]
public class TransactionListTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task The_list_can_be_narrowed_to_a_date_range_that_includes_both_of_its_ends()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddAsync(browser, "expense", everyday, "1", "2026-10-31");
        await AddAsync(browser, "expense", everyday, "2", "2026-11-01");
        await AddAsync(browser, "expense", everyday, "3", "2026-11-15");
        await AddAsync(browser, "expense", everyday, "4", "2026-11-16");

        Assert.Equal(["3.00", "2.00"], await AmountsAsync(browser, "?from=2026-11-01&to=2026-11-15"));
        Assert.Equal(["4.00", "3.00"], await AmountsAsync(browser, "?from=2026-11-15"));
        Assert.Equal(["1.00"], await AmountsAsync(browser, "?to=2026-10-31"));
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_an_account_a_type_and_a_range_of_amounts_that_includes_both_of_its_ends()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "USD");
        var savings = await AddAccountAsync(browser, "Savings", "USD");
        await AddAsync(browser, "expense", everyday, "100");
        await AddAsync(browser, "income", everyday, "200");
        await AddAsync(browser, "expense", savings, "300");
        await AddAsync(browser, "income", savings, "400.5");

        Assert.Equal(["400.50", "300.00"], await AmountsAsync(browser, $"?accountId={savings}"));
        Assert.Equal(["400.50", "200.00"], await AmountsAsync(browser, "?type=income"));
        Assert.Equal(["300.00", "200.00"], await AmountsAsync(browser, "?min=200&max=300.00"));
        Assert.Equal(["400.50"], await AmountsAsync(browser, "?min=300.01"));
        Assert.Equal(["100.00"], await AmountsAsync(browser, "?max=199.99"));
        Assert.Equal(["300.00"], await AmountsAsync(browser, $"?accountId={savings}&type=expense&min=1&max=1000&from=2026-11-02&to=2026-11-02"));
        Assert.Empty(await AmountsAsync(browser, $"?accountId={Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Narrowing_to_a_category_lists_its_subcategories_transactions_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        var groceries = await CategoryAsync(browser, "Groceries");
        await AddAsync(browser, "expense", everyday, "1", category: "Food & drink");
        await AddAsync(browser, "expense", everyday, "2", category: "Groceries");
        await AddAsync(browser, "expense", everyday, "3", category: "Transport");
        await AddAsync(browser, "income", everyday, "4", category: "Salary");

        Assert.Equal(["2.00", "1.00"], await AmountsAsync(browser, $"?categoryId={food}"));
        Assert.Equal(["2.00"], await AmountsAsync(browser, $"?categoryId={groceries}"));
    }

    [Fact]
    public async Task Searching_finds_the_text_anywhere_in_the_payee_or_the_note_whatever_its_case()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddAsync(browser, "expense", everyday, "1", payee: "Co.opmart Nguyễn Đình Chiểu");
        await AddAsync(browser, "expense", everyday, "2", payee: "Grab", note: "ride to the co.opmart");
        await AddAsync(browser, "expense", everyday, "3", payee: "Highlands Coffee", note: "50% off");
        await AddAsync(browser, "expense", everyday, "4");

        Assert.Equal(["2.00", "1.00"], await AmountsAsync(browser, "?q=OPMART"));
        Assert.Equal(["1.00"], await AmountsAsync(browser, $"?q={Uri.EscapeDataString("nguyễn đình")}"));
        Assert.Equal(["3.00"], await AmountsAsync(browser, "?q=%20coffee%20"));
        Assert.Equal(["3.00"], await AmountsAsync(browser, "?q=50%25"));
        Assert.Empty(await AmountsAsync(browser, "?q=co_opmart"));
        Assert.Equal(["2.00"], await AmountsAsync(browser, "?q=co.op&min=2"));
        Assert.Equal(4, (await AmountsAsync(browser, "?q=")).Length);
    }

    [Fact]
    public async Task The_list_can_be_sorted_by_date_or_amount_in_either_direction()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddAsync(browser, "expense", everyday, "30", "2026-11-01");
        await AddAsync(browser, "income", everyday, "10", "2026-11-03");
        await AddAsync(browser, "expense", everyday, "20", "2026-11-02");
        await AddAsync(browser, "expense", everyday, "25", "2026-11-02");

        Assert.Equal(["10.00", "25.00", "20.00", "30.00"], await AmountsAsync(browser, ""));
        Assert.Equal(["10.00", "25.00", "20.00", "30.00"], await AmountsAsync(browser, "?sort=date&order=desc"));
        Assert.Equal(["30.00", "20.00", "25.00", "10.00"], await AmountsAsync(browser, "?sort=date&order=asc"));
        Assert.Equal(["30.00", "25.00", "20.00", "10.00"], await AmountsAsync(browser, "?sort=amount"));
        Assert.Equal(["10.00", "20.00", "25.00", "30.00"], await AmountsAsync(browser, "?sort=amount&order=asc"));
    }

    [Theory]
    [InlineData("sort=date&order=desc", new[] { "2.00", "5.00", "4.00", "2.00", "1.00" })]
    [InlineData("sort=date&order=asc", new[] { "1.00", "2.00", "4.00", "5.00", "2.00" })]
    [InlineData("sort=amount&order=desc", new[] { "5.00", "4.00", "2.00", "2.00", "1.00" })]
    [InlineData("sort=amount&order=asc", new[] { "1.00", "2.00", "2.00", "4.00", "5.00" })]
    public async Task Every_sort_order_comes_in_pages_that_neither_repeat_nor_skip_a_transaction_of_the_same_date_or_amount(string sort, string[] amounts)
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var added = new List<TransactionBody>
        {
            await AddAsync(browser, "expense", everyday, "1", "2026-11-01"),
            await AddAsync(browser, "expense", everyday, "2", "2026-11-02"),
            await AddAsync(browser, "expense", everyday, "4", "2026-11-02"),
            await AddAsync(browser, "expense", everyday, "5", "2026-11-02"),
            await AddAsync(browser, "expense", everyday, "2", "2026-11-03"),
            await AddAsync(browser, "income", everyday, "999", "2026-11-02"),
        };

        var listed = new List<TransactionBody>();
        string? cursor = null;
        do
        {
            var page = await ListAsync(browser, $"?type=expense&limit=2&{sort}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
            Assert.InRange(page.Items.Length, 1, 2);
            listed.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(amounts, listed.Select(transaction => transaction.Amount));
        Assert.Equal(added.Take(5).Select(transaction => transaction.Id).Order(), listed.Select(transaction => transaction.Id).Order());
    }

    [Fact]
    public async Task The_total_is_of_every_transaction_that_passes_the_filter_whichever_page_is_asked_for()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddAsync(browser, "income", everyday, "15000000", "2026-11-01");
        await AddAsync(browser, "expense", everyday, "125000", "2026-11-02");
        await AddAsync(browser, "expense", everyday, "2500000", "2026-11-03");
        await AddAsync(browser, "expense", everyday, "70000", "2026-10-31");
        var deleted = await AddAsync(browser, "expense", everyday, "1", "2026-11-04");
        (await browser.DeleteAsync($"{Transactions}/{deleted.Id}")).EnsureSuccessStatusCode();

        var first = await ListAsync(browser, "?from=2026-11-01&limit=1");
        var second = await ListAsync(browser, $"?from=2026-11-01&limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        var expenses = await ListAsync(browser, "?type=expense&limit=1");
        var none = await ListAsync(browser, "?q=nothing-has-this");

        var november = new TotalBody(3, [new CurrencyTotalBody("VND", "15000000.00", "2625000.00", "12375000.00")]);
        Assert.Single(first.Items);
        Assert.Equal(november, first.Total, TotalBody.Comparer);
        Assert.Equal(november, second.Total, TotalBody.Comparer);
        Assert.Equal(new TotalBody(3, [new CurrencyTotalBody("VND", "0.00", "2695000.00", "-2695000.00")]), expenses.Total, TotalBody.Comparer);
        Assert.Equal(new TotalBody(0, []), none.Total, TotalBody.Comparer);
    }

    [Fact]
    public async Task The_total_adds_up_each_currency_on_its_own()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND");
        var travel = await AddAccountAsync(browser, "Travel", "USD");
        await AddAsync(browser, "expense", everyday, "125000");
        await AddAsync(browser, "expense", travel, "39.5");
        await AddAsync(browser, "income", travel, "100");

        var list = await ListAsync(browser);

        Assert.Equal(
            new TotalBody(3, [new CurrencyTotalBody("VND", "0.00", "125000.00", "-125000.00"), new CurrencyTotalBody("USD", "100.00", "39.50", "60.50")]),
            list.Total, TotalBody.Comparer);
    }

    [Theory]
    [InlineData("from=02/11/2026", "from")]
    [InlineData("to=2026-02-30", "to")]
    [InlineData("accountId=everyday", "accountId")]
    [InlineData("categoryId=7", "categoryId")]
    [InlineData("type=transferOut", "type")]
    [InlineData("type=1", "type")]
    [InlineData("min=abc", "min")]
    [InlineData("max=1,500", "max")]
    [InlineData("min=-1", "min")]
    [InlineData("sort=payee", "sort")]
    [InlineData("order=up", "order")]
    public async Task A_filter_or_sort_order_the_list_cannot_read_is_refused_by_name(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Transactions}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Every_unreadable_filter_is_named_at_once_and_a_search_holds_at_most_200_characters()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Transactions}?from=yesterday&max=lots&q={new string('q', 201)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from", "max", "q"], await response.ErrorFieldsAsync());
        Assert.Empty((await ListAsync(browser, $"?q={new string('q', 200)}")).Items);
    }

    [Fact]
    public async Task Filters_never_reach_another_users_transactions()
    {
        var (owner, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(owner, "Everyday");
        var groceries = await CategoryAsync(owner, "Groceries");
        await AddAsync(owner, "expense", everyday, "125000", payee: "Co.opmart");
        var (stranger, _) = await api.SignedInAsync();

        foreach (var query in new[] { $"?accountId={everyday}", $"?categoryId={groceries}", "?q=co.opmart", "?min=1" })
        {
            var list = await ListAsync(stranger, query);
            Assert.Empty(list.Items);
            Assert.Equal(new TotalBody(0, []), list.Total, TotalBody.Comparer);
        }
    }

    [Fact]
    public async Task A_split_transaction_is_found_by_the_category_of_any_of_its_lines_or_that_categorys_parent_and_by_no_other()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var (groceries, clothes) = (await CategoryAsync(browser, "Groceries"), await CategoryAsync(browser, "Clothes"));
        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId = everyday, amount = "500", date = "2026-11-02",
            lines = new object[] { new { categoryId = groceries, amount = "320" }, new { categoryId = clothes, amount = "180" } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AddAsync(browser, "expense", everyday, "7", category: "Fuel");

        Assert.Equal(["500.00"], await AmountsAsync(browser, $"?categoryId={groceries}"));
        Assert.Equal(["500.00"], await AmountsAsync(browser, $"?categoryId={clothes}"));
        Assert.Equal(["500.00"], await AmountsAsync(browser, $"?categoryId={await CategoryAsync(browser, "Shopping")}"));
        Assert.Equal(["500.00"], await AmountsAsync(browser, $"?categoryId={await CategoryAsync(browser, "Food & drink")}"));
        Assert.Equal(["7.00"], await AmountsAsync(browser, $"?categoryId={await CategoryAsync(browser, "Transport")}"));
        Assert.Empty(await AmountsAsync(browser, $"?categoryId={await CategoryAsync(browser, "Housing")}"));

        // A split found by one of its lines is listed once and counts whole: the total is of transactions, not of lines.
        var found = await ListAsync(browser, $"?categoryId={clothes}");
        Assert.Equal(new TotalBody(1, [new CurrencyTotalBody("VND", "0.00", "500.00", "-500.00")]), found.Total, TotalBody.Comparer);
    }

    [Fact]
    public async Task A_refund_is_money_coming_back_so_the_total_takes_it_off_the_expenses()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var clothes = await CategoryAsync(browser, "Clothes");
        await AddAsync(browser, "income", everyday, "1000");
        await AddAsync(browser, "expense", everyday, "400", category: "Clothes");
        var refund = await browser.PostAsync(
            Transactions, new { type = "expense", isRefund = true, accountId = everyday, amount = "150", date = "2026-11-03", categoryId = clothes });
        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);

        var all = await ListAsync(browser);
        var refunds = await ListAsync(browser, "?max=150");

        Assert.Equal(new TotalBody(3, [new CurrencyTotalBody("VND", "1000.00", "250.00", "750.00")]), all.Total, TotalBody.Comparer);
        Assert.Equal(new TotalBody(1, [new CurrencyTotalBody("VND", "0.00", "-150.00", "150.00")]), refunds.Total, TotalBody.Comparer);
    }

    private static async Task<TransactionBody> AddAsync(
        Browser browser, string type, Guid accountId, string amount, string date = "2026-11-02", string? category = null, string? payee = null,
        string? note = null)
    {
        var categoryId = await CategoryAsync(browser, category ?? (type == "income" ? "Salary" : "Groceries"));
        var response = await browser.PostAsync(Transactions, new { type, accountId, amount, date, categoryId, payee, note });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionList> ListAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Transactions + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionList>())!;
    }

    /// <summary>The amounts of the transactions the list answers, in its order: what tells them apart in these tests.</summary>
    private static async Task<string[]> AmountsAsync(Browser browser, string query) =>
        [.. (await ListAsync(browser, query)).Items.Select(transaction => transaction.Amount)];

    private sealed record TransactionList(TransactionBody[] Items, string? NextCursor, TotalBody Total);

    private sealed record TotalBody(int Count, CurrencyTotalBody[] Currencies)
    {
        public static readonly IEqualityComparer<TotalBody> Comparer = EqualityComparer<TotalBody>.Create(
            (one, other) => one!.Count == other!.Count && one.Currencies.SequenceEqual(other.Currencies), total => total.Count);
    }

    private sealed record CurrencyTotalBody(string Currency, string Income, string Expenses, string Net);

    private sealed record TransactionBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid CategoryId, string? Payee, string? Note, string RowVersion);
}
