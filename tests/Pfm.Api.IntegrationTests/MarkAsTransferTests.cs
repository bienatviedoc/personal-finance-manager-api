using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Mark as transfer: an expense and income on two accounts joined into one transfer, and unmarked again (spec IMP-7; decision record 0001).</summary>
[Collection(ApiCollection.Name)]
public class MarkAsTransferTests(PfmApi api)
{
    private const string Transfers = "/api/v1/transfers";
    private const string Transactions = "/api/v1/transactions";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly string ThisMonth = Today.ToString("yyyy-MM");

    [Fact]
    public async Task An_expense_and_income_on_another_account_become_the_two_legs_of_one_transfer()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var visa = await AddAccountAsync(browser, "Visa", "VND");
        var expense = await RecordAsync(browser, "expense", everyday, "Bank fees", "1500000", Today, payee: "VISA PAYMENT");
        var income = await RecordAsync(browser, "income", visa, "Other income", "1500000", Today);

        var response = await MarkAsync(browser, expense, income);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferBody>())!;
        Assert.Equal((everyday, visa, "1500000.00", "1500000.00", Today.ToString("yyyy-MM-dd")), (transfer.FromAccountId, transfer.ToAccountId, transfer.Amount, transfer.ToAmount, transfer.Date));
        Assert.Equal((expense, income), (transfer.OutTransactionId, transfer.InTransactionId));
        Assert.Equal($"{Transfers}/{transfer.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal($"\"{transfer.RowVersion}\"", response.Headers.ETag?.Tag);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"{Transfers}/{transfer.Id}")).StatusCode);
        Assert.Equal(
            [
                new LegBody(expense, "transferOut", everyday, "1500000.00", null, null, transfer.Id, []),
                new LegBody(income, "transferIn", visa, "1500000.00", null, null, transfer.Id, []),
            ],
            (await ListAsync(browser)).Items.OrderByDescending(leg => leg.Type));
        Assert.Equal(("3500000.00", "1500000.00"), (await BalanceAsync(browser, everyday), await BalanceAsync(browser, visa)));
    }

    [Fact]
    public async Task Once_marked_the_two_drop_out_of_income_expense_and_budget_totals()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var food = await CategoryAsync(browser, "Food & drink");
        Assert.Equal(HttpStatusCode.Created, (await browser.PutAsync($"/api/v1/budgets/{ThisMonth}/{food}", new { amount = "3000000" })).StatusCode);
        await SpendAsync(browser, everyday, "Groceries", "120000", Today);
        var expense = await SpendAsync(browser, everyday, "Eating out", "2000000", Today);
        var income = await RecordAsync(browser, "income", savings, "Other income", "2000000", Today);
        Assert.Equal("2120000.00", (await MonthAsync(browser)).Totals.Spent);

        (await MarkAsync(browser, expense, income)).EnsureSuccessStatusCode();

        Assert.Equal("120000.00", (await MonthAsync(browser)).Totals.Spent);
        var total = (await ListAsync(browser)).Total;
        Assert.Equal((3, "0.00", "120000.00"), (total.Count, total.Currencies.Single().Income, total.Currencies.Single().Expenses));
    }

    [Fact]
    public async Task Between_accounts_in_different_currencies_each_leg_keeps_its_own_amount()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD");
        var expense = await SpendAsync(browser, everyday, "Bank fees", "2550000", Today);
        var income = await RecordAsync(browser, "income", dollars, "Other income", "99.5", Today);

        var transfer = (await (await MarkAsync(browser, expense, income)).Content.ReadFromJsonAsync<TransferBody>())!;

        Assert.Equal(("2550000.00", "VND", "99.50", "USD"), (transfer.Amount, transfer.Currency, transfer.ToAmount, transfer.ToCurrency));
    }

    [Fact]
    public async Task It_is_refused_when_both_are_on_one_account_or_either_is_a_leg_of_a_transfer_already_and_nothing_changes()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var visa = await AddAccountAsync(browser, "Visa");
        var expense = await SpendAsync(browser, everyday, "Bank fees", "100", Today);
        var sameAccount = await RecordAsync(browser, "income", everyday, "Other income", "100", Today);
        var income = await RecordAsync(browser, "income", savings, "Other income", "100", Today);
        var otherIncome = await RecordAsync(browser, "income", visa, "Other income", "100", Today);

        var oneAccount = await MarkAsync(browser, expense, sameAccount);
        var marked = (await (await MarkAsync(browser, expense, income)).Content.ReadFromJsonAsync<TransferBody>())!;
        var expenseALeg = await MarkAsync(browser, expense, otherIncome);
        var incomeALeg = await MarkAsync(browser, await SpendAsync(browser, visa, "Bank fees", "100", Today), income);
        var outLegAsIncome = await MarkAsync(browser, await SpendAsync(browser, visa, "Bank fees", "100", Today), expense);

        Assert.Equal(HttpStatusCode.BadRequest, oneAccount.StatusCode);
        Assert.Equal(["incomeId"], await oneAccount.ErrorFieldsAsync());
        Assert.Equal(["expenseId"], await expenseALeg.ErrorFieldsAsync());
        Assert.Equal(["incomeId"], await incomeALeg.ErrorFieldsAsync());
        Assert.Equal(["incomeId"], await outLegAsIncome.ErrorFieldsAsync());
        var listed = (await ListAsync(browser)).Items;
        Assert.Equal("income", listed.Single(transaction => transaction.Id == otherIncome).Type);
        Assert.Equal("income", listed.Single(transaction => transaction.Id == sameAccount).Type);
        Assert.Equal(marked.Id, listed.Single(transaction => transaction.Id == expense).TransferId);
    }

    [Fact]
    public async Task It_takes_an_expense_that_is_not_a_refund_and_income_that_match_in_amount_and_date()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var income = await RecordAsync(browser, "income", savings, "Other income", "100", Today);

        var refund = await MarkAsync(browser, await RefundAsync(browser, everyday, "Bank fees", "100", Today), income);
        var swapped = await MarkAsync(browser, income, await SpendAsync(browser, everyday, "Bank fees", "100", Today));
        var otherAmount = await MarkAsync(browser, await SpendAsync(browser, everyday, "Bank fees", "90", Today), income);
        var otherDate = await MarkAsync(browser, await SpendAsync(browser, everyday, "Bank fees", "100", Today.AddDays(-1)), income);

        Assert.Equal(["expenseId"], await refund.ErrorFieldsAsync());
        Assert.Equal(["expenseId"], await swapped.ErrorFieldsAsync());
        Assert.Equal(["incomeId"], await otherAmount.ErrorFieldsAsync());
        Assert.Equal(["incomeId"], await otherDate.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_transaction_that_is_not_the_users_own_or_is_deleted_cannot_be_marked()
    {
        var (browser, _) = await api.SignedInAsync();
        var (stranger, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var expense = await SpendAsync(browser, everyday, "Bank fees", "100", Today);
        var theirs = await RecordAsync(stranger, "income", await AddAccountAsync(stranger, "Theirs"), "Other income", "100", Today);
        var deleted = await RecordAsync(browser, "income", await AddAccountAsync(browser, "Savings"), "Other income", "100", Today);
        await browser.DeleteAsync($"{Transactions}/{deleted}");

        var response = await MarkAsync(browser, expense, theirs);
        var none = await browser.PostAsync($"{Transfers}/mark", new { });
        var deletedOne = await MarkAsync(browser, expense, deleted);

        Assert.Equal(["incomeId"], await response.ErrorFieldsAsync());
        Assert.Equal(["expenseId", "incomeId"], await none.ErrorFieldsAsync());
        Assert.Equal(["incomeId"], await deletedOne.ErrorFieldsAsync());
        Assert.Equal("income", (await ListAsync(stranger)).Items.Single().Type);
    }

    [Fact]
    public async Task A_marked_transfer_is_unmarked_into_an_expense_and_income_again_on_the_categories_chosen()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var expense = await SpendAsync(browser, everyday, "Eating out", "2000000", Today);
        var income = await RecordAsync(browser, "income", savings, "Other income", "2000000", Today);
        var transfer = (await (await MarkAsync(browser, expense, income)).Content.ReadFromJsonAsync<TransferBody>())!;
        var fees = await CategoryAsync(browser, "Bank fees");
        var gifts = await CategoryAsync(browser, "Gifts received");

        var response = await browser.PostAsync($"{Transfers}/{transfer.Id}/unmark", new { expenseCategoryId = fees, incomeCategoryId = gifts });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unmarked = (await response.Content.ReadFromJsonAsync<UnmarkedBody>())!;
        Assert.Equal(
            (expense, "expense", (Guid?)fees, income, "income", (Guid?)gifts),
            (unmarked.Expense.Id, unmarked.Expense.Type, unmarked.Expense.CategoryId, unmarked.Income.Id, unmarked.Income.Type, unmarked.Income.CategoryId));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Transfers}/{transfer.Id}")).StatusCode);
        var listed = (await ListAsync(browser)).Items.OrderBy(transaction => transaction.Type).ToArray();
        Assert.Equal(
            [
                new LegBody(expense, "expense", everyday, "2000000.00", fees, null, null, [new LineBody(fees, "2000000.00")]),
                new LegBody(income, "income", savings, "2000000.00", gifts, null, null, [new LineBody(gifts, "2000000.00")]),
            ],
            listed);
        var total = (await ListAsync(browser)).Total.Currencies.Single();
        Assert.Equal(("2000000.00", "2000000.00"), (total.Income, total.Expenses));
        Assert.Equal(("3000000.00", "2000000.00"), (await BalanceAsync(browser, everyday), await BalanceAsync(browser, savings)));
    }

    [Fact]
    public async Task Unmarking_takes_categories_of_the_right_kind_and_a_transfer_that_is_not_there_is_not_found()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var transfer = (await (await MarkAsync(
            browser, await SpendAsync(browser, everyday, "Bank fees", "100", Today), await RecordAsync(browser, "income", savings, "Other income", "100", Today)))
            .Content.ReadFromJsonAsync<TransferBody>())!;
        var fees = await CategoryAsync(browser, "Bank fees");
        var gifts = await CategoryAsync(browser, "Gifts received");

        var wrongKinds = await browser.PostAsync($"{Transfers}/{transfer.Id}/unmark", new { expenseCategoryId = gifts, incomeCategoryId = fees });
        var none = await browser.PostAsync($"{Transfers}/{transfer.Id}/unmark", new { });
        var missing = await browser.PostAsync($"{Transfers}/{Guid.CreateVersion7()}/unmark", new { expenseCategoryId = fees, incomeCategoryId = gifts });

        Assert.Equal(HttpStatusCode.BadRequest, wrongKinds.StatusCode);
        Assert.Equal(["expenseCategoryId"], await wrongKinds.ErrorFieldsAsync());
        Assert.Equal(["expenseCategoryId", "incomeCategoryId"], await none.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"{Transfers}/{transfer.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_transfer_recorded_as_one_can_be_unmarked_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "100", Today);
        var transferId = (await ListAsync(browser)).Items.First().TransferId!.Value;

        var response = await browser.PostAsync(
            $"{Transfers}/{transferId}/unmark", new { expenseCategoryId = await CategoryAsync(browser, "Bank fees"), incomeCategoryId = await CategoryAsync(browser, "Other income") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["expense", "income"], (await ListAsync(browser)).Items.Select(transaction => transaction.Type).Order());
    }

    private static Task<HttpResponseMessage> MarkAsync(Browser browser, Guid expenseId, Guid incomeId) =>
        browser.PostAsync($"{Transfers}/mark", new { expenseId, incomeId });

    private static async Task<TransactionList> ListAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<TransactionList>(Transactions))!;

    private static async Task<MonthBody> MonthAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<MonthBody>($"/api/v1/budgets?month={ThisMonth}"))!;

    private sealed record TransactionList(LegBody[] Items, TotalBody Total);

    private sealed record TotalBody(int Count, CurrencyTotalBody[] Currencies);

    private sealed record CurrencyTotalBody(string Currency, string Income, string Expenses);

    private sealed record MonthBody(MonthTotalsBody Totals);

    private sealed record MonthTotalsBody(string Spent);

    private sealed record LineBody(Guid CategoryId, string Amount);

    /// <summary>A transaction as the list answers it.</summary>
    private sealed record LegBody(Guid Id, string Type, Guid AccountId, string Amount, Guid? CategoryId, string? Payee, Guid? TransferId, LineBody[] Lines)
    {
        public bool Equals(LegBody? other) =>
            other is not null && (Id, Type, AccountId, Amount, CategoryId, Payee, TransferId) == (other.Id, other.Type, other.AccountId, other.Amount, other.CategoryId, other.Payee, other.TransferId)
            && Lines.SequenceEqual(other.Lines);

        public override int GetHashCode() => Id.GetHashCode();
    }

    private sealed record UnmarkedBody(LegBody Expense, LegBody Income);

    private sealed record TransferBody(
        Guid Id, Guid FromAccountId, Guid ToAccountId, string Amount, string Currency, string ToAmount, string ToCurrency, string Date, Guid OutTransactionId,
        Guid InTransactionId, string RowVersion);
}
