using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>A transaction split across several categories (spec section 3, rule 5; TXN-4).</summary>
[Collection(ApiCollection.Name)]
public class SplitTransactionTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task A_user_can_split_an_expense_across_categories_each_line_with_an_amount_and_an_optional_note()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var (groceries, shopping) = (await CategoryAsync(browser, "Groceries"), await CategoryAsync(browser, "Shopping"));

        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId = everyday, amount = "500000", date = "2026-11-02", payee = "Co.opmart",
            lines = new object[] { new { categoryId = groceries, amount = "320000", note = "  food " }, new { categoryId = shopping, amount = "180000.00" } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var split = (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
        Assert.Equal(("500000.00", groceries), (split.Amount, split.CategoryId));
        Assert.Equal([new LineBody(groceries, "320000.00", "food"), new LineBody(shopping, "180000.00", null)], split.Lines);
        Assert.Equal(split.Lines, (await GetAsync(browser, split.Id)).Lines);
        Assert.Equal(split.Lines, Assert.Single((await PageAsync(browser)).Items).Lines);
        Assert.Equal("500000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_transaction_that_is_not_split_answers_one_line_with_its_category_and_whole_amount()
    {
        var (browser, _) = await api.SignedInAsync();
        var groceries = await CategoryAsync(browser, "Groceries");

        var transaction = await RecordAsync(browser, new { type = "expense", accountId = await AddAccountAsync(browser, "Everyday"), amount = "125000", date = "2026-11-02", categoryId = groceries });

        Assert.Equal([new LineBody(groceries, "125000.00", null)], transaction.Lines);
    }

    [Theory]
    [InlineData("320000", "179999")]
    [InlineData("320000", "180001")]
    public async Task Lines_that_do_not_sum_exactly_to_the_amount_are_refused_and_nothing_is_recorded(string first, string second)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Transactions, await ASplitAsync(browser, "500000", first, second));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["lines"], await response.ErrorFieldsAsync());
        Assert.Empty((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task Every_line_takes_one_of_the_users_categories_of_the_kind_the_transactions_type_takes()
    {
        var (browser, _) = await api.SignedInAsync();
        var split = await ASplitAsync(browser, "500000", "320000", "180000");

        var income = await browser.PostAsync(Transactions, WithLine(split, 1, new { categoryId = await CategoryAsync(browser, "Salary"), amount = "180000" }));
        var unknown = await browser.PostAsync(Transactions, WithLine(split, 0, new { categoryId = Guid.CreateVersion7(), amount = "320000" }));

        Assert.Equal(HttpStatusCode.BadRequest, income.StatusCode);
        Assert.Equal(["lines[1].categoryId"], await income.ErrorFieldsAsync());
        Assert.Equal(["lines[0].categoryId"], await unknown.ErrorFieldsAsync());
        Assert.Empty((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task A_line_cannot_take_an_archived_category_or_another_users()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobsGroceries = await CategoryAsync(bob, "Groceries");
        var (browser, _) = await api.SignedInAsync();
        var split = await ASplitAsync(browser, "500000", "320000", "180000");
        var travel = await CategoryAsync(browser, "Travel");
        (await browser.DeleteAsync($"/api/v1/categories/{travel}")).EnsureSuccessStatusCode();

        var archived = await browser.PostAsync(Transactions, WithLine(split, 1, new { categoryId = travel, amount = "180000" }));
        var bobs = await browser.PostAsync(Transactions, WithLine(split, 1, new { categoryId = bobsGroceries, amount = "180000" }));

        Assert.Equal(["lines[1].categoryId"], await archived.ErrorFieldsAsync());
        Assert.Equal(["lines[1].categoryId"], await bobs.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_split_needs_at_least_two_lines_each_with_a_category_and_an_amount_above_zero_that_fits_the_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var split = await ASplitAsync(browser, "500000", "320000", "180000");
        var groceries = await CategoryAsync(browser, "Groceries");

        var oneLine = await browser.PostAsync(Transactions, new Dictionary<string, object?>(split) { ["lines"] = new object[] { new { categoryId = groceries, amount = "500000" } } });
        var noLines = await browser.PostAsync(Transactions, new Dictionary<string, object?>(split) { ["lines"] = Array.Empty<object>() });
        var incomplete = await browser.PostAsync(Transactions, WithLine(split, 0, new { note = new string('n', 1001) }));
        var zero = await browser.PostAsync(Transactions, WithLine(split, 1, new { categoryId = groceries, amount = "0" }));
        var halfDong = await browser.PostAsync(Transactions, new Dictionary<string, object?>(split)
        {
            ["lines"] = new object[] { new { categoryId = groceries, amount = "319999.5" }, new { categoryId = groceries, amount = "180000.5" } },
        });
        var nothing = await browser.PostAsync(Transactions, new Dictionary<string, object?>(split) { ["lines"] = new object?[] { null, null } });

        Assert.Equal(["lines"], await oneLine.ErrorFieldsAsync());
        Assert.Equal(["lines"], await noLines.ErrorFieldsAsync());
        Assert.Equal(["lines[0].amount", "lines[0].categoryId", "lines[0].note"], await incomplete.ErrorFieldsAsync());
        Assert.Equal(["lines[1].amount"], await zero.ErrorFieldsAsync());
        Assert.Equal(["lines[0].amount"], await halfDong.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Empty((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task A_split_names_its_categories_on_its_lines_and_none_of_its_own()
    {
        var (browser, _) = await api.SignedInAsync();
        var split = await ASplitAsync(browser, "500000", "320000", "180000");
        split["categoryId"] = await CategoryAsync(browser, "Groceries");

        var response = await browser.PostAsync(Transactions, split);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["categoryId"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_edit_can_split_a_transaction_change_its_lines_and_make_it_one_category_again()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var (groceries, shopping, travel) = (await CategoryAsync(browser, "Groceries"), await CategoryAsync(browser, "Shopping"), await CategoryAsync(browser, "Travel"));
        var transaction = await RecordAsync(browser, new { type = "expense", accountId = everyday, amount = "500000", date = "2026-11-02", categoryId = groceries });
        var path = $"{Transactions}/{transaction.Id}";

        var split = await EditAsync(browser, path, transaction.RowVersion, new
        {
            type = "expense", accountId = everyday, amount = "500000", date = "2026-11-02",
            lines = new object[] { new { categoryId = shopping, amount = "300000" }, new { categoryId = groceries, amount = "200000", note = "food" } },
        });
        var changed = await EditAsync(browser, path, split.RowVersion, new
        {
            type = "expense", accountId = everyday, amount = "600000", date = "2026-11-02",
            lines = new object[]
            {
                new { categoryId = shopping, amount = "300000" }, new { categoryId = groceries, amount = "200000" }, new { categoryId = travel, amount = "100000" },
            },
        });
        var whole = await EditAsync(browser, path, changed.RowVersion, new { type = "expense", accountId = everyday, amount = "450000", date = "2026-11-02", categoryId = travel });

        Assert.Equal([new LineBody(shopping, "300000.00", null), new LineBody(groceries, "200000.00", "food")], split.Lines);
        Assert.Equal(shopping, split.CategoryId);
        Assert.Equal(
            [new LineBody(shopping, "300000.00", null), new LineBody(groceries, "200000.00", null), new LineBody(travel, "100000.00", null)], changed.Lines);
        Assert.Equal([new LineBody(travel, "450000.00", null)], whole.Lines);
        Assert.Equal(whole, await GetAsync(browser, transaction.Id) with { RowVersion = whole.RowVersion });
        Assert.Equal("550000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task An_edit_whose_lines_do_not_sum_to_the_amount_is_refused_and_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var request = await ASplitAsync(browser, "500000", "320000", "180000");
        var split = await RecordAsync(browser, request);
        request["amount"] = "600000";

        var response = await browser.PutAsync($"{Transactions}/{split.Id}", request, ifMatch: $"\"{split.RowVersion}\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["lines"], await response.ErrorFieldsAsync());
        Assert.Equal(split, await GetAsync(browser, split.Id));
    }

    [Fact]
    public async Task A_categorys_kind_is_fixed_once_any_line_of_a_split_is_on_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var shopping = await CategoryAsync(browser, "Shopping");
        await RecordAsync(browser, await ASplitAsync(browser, "500000", "320000", "180000"));

        var response = await MakeShoppingIncomeAsync(browser, shopping);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["kind"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_split_recorded_while_the_kind_of_one_of_its_lines_categories_changes_is_refused_as_the_category_no_longer_takes_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var shopping = await CategoryAsync(browser, "Shopping");
        var split = await ASplitAsync(browser, "500000", "320000", "180000");
        var changed = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Transaction>(async () => changed = (await MakeShoppingIncomeAsync(browser, shopping)).StatusCode);

        var recorded = await browser.PostAsync(Transactions, split);

        Assert.Equal(HttpStatusCode.OK, changed);
        Assert.Equal(HttpStatusCode.BadRequest, recorded.StatusCode);
        Assert.Equal(["lines[1].categoryId"], await recorded.ErrorFieldsAsync());
        Assert.Empty((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task A_retry_of_a_split_with_its_idempotency_key_records_it_once_and_other_lines_under_that_key_are_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var split = await ASplitAsync(browser, "500000", "320000", "180000");

        var first = await browser.PostAsync(Transactions, split, idempotencyKey: "split-1");
        var retry = await browser.PostAsync(Transactions, await ASplitAsync(browser, "500000.00", "320000.0", "180000", account: (Guid)split["accountId"]!), idempotencyKey: "split-1");
        var otherLines = await browser.PostAsync(Transactions, await ASplitAsync(browser, "500000", "300000", "200000", account: (Guid)split["accountId"]!), idempotencyKey: "split-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<TransactionBody>(), await retry.Content.ReadFromJsonAsync<TransactionBody>());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otherLines.StatusCode);
        Assert.Single((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task Deleting_a_split_takes_its_whole_amount_out_of_the_balance()
    {
        var (browser, _) = await api.SignedInAsync();
        var request = await ASplitAsync(browser, "500000", "320000", "180000");
        var split = await RecordAsync(browser, request);

        (await browser.DeleteAsync($"{Transactions}/{split.Id}")).EnsureSuccessStatusCode();

        Assert.Equal("0.00", await BalanceAsync(browser, (Guid)request["accountId"]!));
        Assert.Empty((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task A_refund_can_be_split_and_raises_the_balance_by_its_whole_amount()
    {
        var (browser, _) = await api.SignedInAsync();
        var request = await ASplitAsync(browser, "500000", "320000", "180000");
        request["isRefund"] = true;

        var refund = await RecordAsync(browser, request);

        Assert.Equal(2, refund.Lines.Length);
        Assert.Equal("500000.00", await BalanceAsync(browser, (Guid)request["accountId"]!));
    }

    /// <summary>An expense on a new account, split between Groceries and Shopping.</summary>
    private static async Task<Dictionary<string, object?>> ASplitAsync(Browser browser, string amount, string groceries, string shopping, Guid? account = null) =>
        new()
        {
            ["type"] = "expense", ["accountId"] = account ?? await AddAccountAsync(browser, "Everyday"), ["amount"] = amount, ["date"] = "2026-11-02",
            ["lines"] = new object[]
            {
                new { categoryId = await CategoryAsync(browser, "Groceries"), amount = groceries },
                new { categoryId = await CategoryAsync(browser, "Shopping"), amount = shopping },
            },
        };

    private static Dictionary<string, object?> WithLine(Dictionary<string, object?> split, int position, object line)
    {
        var lines = (object[])((object[])split["lines"]!).Clone();
        lines[position] = line;
        return new(split) { ["lines"] = lines };
    }

    private static Task<HttpResponseMessage> MakeShoppingIncomeAsync(Browser browser, Guid shopping) =>
        browser.PutAsync($"/api/v1/categories/{shopping}", new { name = "Shopping", kind = "income", icon = "shopping-bag", color = "#ea580c" });

    private static async Task<TransactionBody> RecordAsync(Browser browser, object transaction)
    {
        var response = await browser.PostAsync(Transactions, transaction);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> EditAsync(Browser browser, string path, string rowVersion, object transaction)
    {
        var response = await browser.PutAsync(path, transaction, ifMatch: $"\"{rowVersion}\"");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> GetAsync(Browser browser, Guid id) =>
        (await browser.Http.GetFromJsonAsync<TransactionBody>($"{Transactions}/{id}"))!;

    private static async Task<TransactionList> PageAsync(Browser browser) => (await browser.Http.GetFromJsonAsync<TransactionList>(Transactions))!;

    private sealed record TransactionList(TransactionBody[] Items);

    private sealed record TransactionBody(Guid Id, string Amount, Guid CategoryId, string RowVersion, LineBody[] Lines)
    {
        public bool Equals(TransactionBody? other) =>
            other is not null && (Id, Amount, CategoryId, RowVersion) == (other.Id, other.Amount, other.CategoryId, other.RowVersion) && Lines.SequenceEqual(other.Lines);

        public override int GetHashCode() => Id.GetHashCode();
    }

    private sealed record LineBody(Guid CategoryId, string Amount, string? Note);
}
