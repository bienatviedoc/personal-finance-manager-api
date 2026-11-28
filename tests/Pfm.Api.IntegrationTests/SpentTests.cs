using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;
using Pfm.Infrastructure.SampleData;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Spent is added up by the database (spec section 6, BUD-8; section 8): the list's total, budgets and every report
/// answer the same spent, by its one definition (`Transaction.SpentAmountIn`): refunds lower it, a split counts only
/// its lines in the category, its converted amount shared among them, transfers and income never count, and deleted
/// transactions are left out.
/// </summary>
[Collection(ApiCollection.Name)]
public class SpentTests(PfmApi api) : IAsyncLifetime
{
    private const string InMarch = "from=2026-03-01&to=2026-03-31";

    private static readonly DateOnly March = new(2026, 3, 1);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_list_budgets_and_every_report_answer_the_same_spent_with_splits_refunds_transfers_deletions_and_other_currencies()
    {
        var (browser, _) = await api.SignedInAsync();
        var data = await RecordEveryKindAsync(browser);
        await SetBudgetAsync(browser, "2026-03", data.Groceries, "1000000");

        // 500,000 - 30,000 refunded + 100,000 of a split + 83,253 of 10 USD split three ways (250,010 at 25,001)
        // + 24,799 left to the last line of 1 USD split (25,050, of which the first line's 0.01 takes 250.5, rounded
        // away from zero to 251) - 62,625 refunded from 2.50 USD. The transfer, the income and the deleted expense
        // add nothing.
        const string groceries = "615427.00";
        var list = await GetAsync<ListBody>(browser, $"/api/v1/transactions?{InMarch}&categoryId={data.Groceries}");
        Assert.Equal(new SpentBody("VND", groceries, 0), list.Total.Spent);

        // Filtered further, by what lines do not carry: only the dollar expenses are up to 1,000 in their own currency.
        var upTo1000 = await GetAsync<ListBody>(browser, $"/api/v1/transactions?{InMarch}&categoryId={data.Groceries}&max=1000");
        Assert.Equal(new SpentBody("VND", "45427.00", 0), upTo1000.Total.Spent);
        Assert.Equal(groceries, (await GetAsync<MonthBody>(browser, "/api/v1/budgets?month=2026-03")).Items.Single(item => item.CategoryId == data.Groceries).Spent);
        Assert.Equal(groceries, (await GetAsync<DrillDownBody>(browser, $"/api/v1/reports/spending-by-category/transactions?{InMarch}&categoryId={data.Groceries}")).Total);
        Assert.Equal(groceries, (await GetAsync<TrendBody>(browser, $"/api/v1/reports/trends?{InMarch}&categoryId={data.Groceries}")).Total);
        Assert.Equal(groceries, (await GetAsync<IncomeVsExpensesBody>(browser, $"/api/v1/reports/income-vs-expenses?{InMarch}&categoryId={data.Groceries}")).Total.Expenses);
        Assert.Equal(groceries, (await GetAsync<BudgetVsActualBody>(browser, $"/api/v1/reports/budget-vs-actual?{InMarch}")).Items.Single(item => item.Category.Id == data.Groceries).Spent);

        // Coffee has the other lines of the two dollar splits: 83,253 and 251. Food & drink is both.
        var food = await CategoryAsync(browser, "Food & drink");
        var byCategory = await GetAsync<SpendingBody>(browser, $"/api/v1/reports/spending-by-category?{InMarch}&parentId={food}");
        Assert.Equal(
            [("Groceries", groceries), ("Coffee", "83504.00")],
            byCategory.Items.Select(item => (item.Category.Name, item.Spent)));
        Assert.Equal("698931.00", (await GetAsync<TrendBody>(browser, $"/api/v1/reports/trends?{InMarch}&categoryId={food}")).Total);

        // Every expense of the month, in VND and converted from USD; the income is apart and the transfer is neither.
        var month = (await GetAsync<IncomeVsExpensesBody>(browser, $"/api/v1/reports/income-vs-expenses?{InMarch}")).Total;
        Assert.Equal(("30000000.00", "832435.00", 0), (month.Income, month.Expenses, month.NotConverted));
    }

    [Fact]
    public async Task An_expense_without_a_rate_yet_adds_nothing_to_spent_and_is_counted_as_not_converted_once_however_it_is_split()
    {
        var (browser, _) = await api.SignedInAsync();
        var data = await RecordEveryKindAsync(browser);
        const string inFebruary = "from=2026-02-01&to=2026-02-28";

        var list = await GetAsync<ListBody>(browser, $"/api/v1/transactions?{inFebruary}&categoryId={data.Groceries}");
        var byCategory = await GetAsync<SpendingBody>(browser, $"/api/v1/reports/spending-by-category?{inFebruary}&parentId={await CategoryAsync(browser, "Food & drink")}");

        Assert.Equal(new SpentBody("VND", "40000.00", 2), list.Total.Spent);
        Assert.Equal(new SpentBody("VND", "0.00", 2), (await GetAsync<ListBody>(browser, $"/api/v1/transactions?{inFebruary}&categoryId={data.Groceries}&max=10")).Total.Spent);
        Assert.Equal(new SpentBody("VND", "0.00", 0), (await GetAsync<ListBody>(browser, $"/api/v1/transactions?{inFebruary}&categoryId={data.Groceries}&type=income")).Total.Spent);
        Assert.Equal([("Groceries", "40000.00", 2), ("Coffee", "0.00", 1)], byCategory.Items.Select(item => (item.Category.Name, item.Spent, item.NotConverted)));
    }

    /// <summary>
    /// The view spent is added up from is user-owned like the transactions it reads: another user's split lines, in
    /// the same days and in categories of the same names, never reach a user's spent, list totals or reports, even
    /// when the user names the other user's category.
    /// </summary>
    [Fact]
    public async Task Another_users_split_lines_never_count_in_a_users_spent_lists_or_reports()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobs = await RecordEveryKindAsync(bob);
        var (alice, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday", "VND", "1000000");
        await SplitAsync(alice, everyday, "150000", March.AddDays(3), ("Groceries", "100000"), ("Coffee", "50000"));
        var groceries = await CategoryAsync(alice, "Groceries");
        var food = await CategoryAsync(alice, "Food & drink");
        await SetBudgetAsync(alice, "2026-03", groceries, "1000000");

        var list = await GetAsync<ListBody>(alice, $"/api/v1/transactions?{InMarch}&categoryId={groceries}");
        Assert.Equal((1, new SpentBody("VND", "100000.00", 0)), (list.Total.Count, list.Total.Spent));
        Assert.Equal("100000.00", (await GetAsync<DrillDownBody>(alice, $"/api/v1/reports/spending-by-category/transactions?{InMarch}&categoryId={groceries}")).Total);
        Assert.Equal("150000.00", (await GetAsync<TrendBody>(alice, $"/api/v1/reports/trends?{InMarch}&categoryId={food}")).Total);
        Assert.Equal(
            [("Groceries", "100000.00"), ("Coffee", "50000.00")],
            (await GetAsync<SpendingBody>(alice, $"/api/v1/reports/spending-by-category?{InMarch}&parentId={food}")).Items.Select(item => (item.Category.Name, item.Spent)));
        var flow = (await GetAsync<IncomeVsExpensesBody>(alice, $"/api/v1/reports/income-vs-expenses?{InMarch}")).Total;
        Assert.Equal(("0.00", "150000.00", 0), (flow.Income, flow.Expenses, flow.NotConverted));
        Assert.Equal("100000.00", (await GetAsync<MonthBody>(alice, "/api/v1/budgets?month=2026-03")).Items.Single(item => item.CategoryId == groceries).Spent);
        Assert.Equal("100000.00", (await GetAsync<BudgetVsActualBody>(alice, $"/api/v1/reports/budget-vs-actual?{InMarch}")).Items.Single(item => item.Category.Id == groceries).Spent);

        var bobsGroceries = await GetAsync<ListBody>(alice, $"/api/v1/transactions?{InMarch}&categoryId={bobs.Groceries}");
        Assert.Equal((0, new SpentBody("VND", "0.00", 0)), (bobsGroceries.Total.Count, bobsGroceries.Total.Spent));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/reports/spending-by-category/transactions?{InMarch}&categoryId={bobs.Groceries}")).StatusCode);
    }

    /// <summary>
    /// The database adds up spent by the same definition as the domain: on the same transactions, what each contract
    /// answers is what `Transaction.SpentAmountIn` and `IncomeAmountIn` work out, rounding of a converted split included.
    /// </summary>
    [Fact]
    public async Task What_the_database_adds_up_is_what_the_domain_works_out_on_the_same_transactions()
    {
        var (browser, email) = await api.SignedInAsync();
        await RecordEveryKindAsync(browser);
        var sample = await api.Services.SeedSampleDataAsync(new SampleDataOptions(PfmApi.NewEmail(), PfmApi.Password, Transactions: 2_000));

        await AssertAgreesWithTheDomainAsync(await UserIdAsync(email), new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30));
        await AssertAgreesWithTheDomainAsync(sample.UserId, sample.From, sample.To);
    }

    private async Task AssertAgreesWithTheDomainAsync(Guid userId, DateOnly from, DateOnly to)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var actingFor = services.GetRequiredService<IActForUser>().ActFor(userId);
        var db = services.GetRequiredService<PfmDbContext>();
        var transactions = await db.Set<Transaction>().AsNoTracking().Where(transaction => transaction.Date >= from && transaction.Date <= to).ToListAsync();
        var counted = transactions.Where(transaction => !transaction.IsDeleted).ToList();
        var expenses = counted.Where(transaction => transaction.Type == TransactionType.Expense).ToList();
        var categories = await db.Set<Category>().AsNoTracking().ToListAsync();
        Assert.Contains(expenses, expense => expense.IsSplit);
        Assert.Contains(expenses, expense => expense.IsRefund);

        var spending = services.GetRequiredService<ICategorySpending>();
        Assert.Equal(
            SpentByCategory(expenses).OrderBy(spent => spent.CategoryId).Select(Text),
            (await spending.SpentByCategoryAsync(from, to, null, default)).OrderBy(spent => spent.CategoryId).Select(Text));
        Assert.Equal(
            expenses.GroupBy(expense => new DateOnly(expense.Date.Year, expense.Date.Month, 1))
                .SelectMany(month => SpentByCategory([.. month]).Select(spent => $"{month.Key} {Text(spent)}")).Order(StringComparer.Ordinal),
            (await spending.SpentByCategoryEachMonthAsync(from, to, null, default)).Select(spent => $"{spent.Month} {Text(spent.Spent)}").Order(StringComparer.Ordinal));
        foreach (var category in expenses.SelectMany(expense => expense.Lines).Select(line => line.CategoryId).Distinct())
        {
            Assert.Equal(
                expenses.Where(expense => expense.Lines.Any(line => line.CategoryId == category))
                    .Select(expense => $"{expense.Id} {(expense.IsConverted ? expense.SpentAmountIn([category]) : null)}").Order(StringComparer.Ordinal),
                (await spending.ExpensesInAsync(from, to, category, null, default)).Select(expense => $"{expense.TransactionId} {expense.Spent}").Order(StringComparer.Ordinal));
        }

        var money = services.GetRequiredService<IIncomeAndSpending>();
        Assert.Equal(
            counted.Where(transaction => transaction.Type is TransactionType.Income or TransactionType.Expense)
                .Select(transaction => Text(transaction, [.. transaction.Lines.Select(line => line.CategoryId)])).Order(StringComparer.Ordinal),
            (await money.IncomeAndExpensesAsync(from, to, null, null, default)).Select(Text).Order(StringComparer.Ordinal));

        var trends = services.GetRequiredService<ITransactionTrends>();
        foreach (var parent in categories.Where(category => category.ParentId is null))
        {
            Guid[] withSubcategories = [parent.Id, .. categories.Where(category => category.ParentId == parent.Id).Select(category => category.Id)];
            var type = parent.Kind == CategoryKind.Income ? TransactionType.Income : TransactionType.Expense;
            var inThem = counted.Where(transaction => transaction.Type == type && transaction.Lines.Any(line => withSubcategories.Contains(line.CategoryId))).ToList();
            Assert.Equal(
                inThem.Select(transaction => Text(transaction, withSubcategories)).Order(StringComparer.Ordinal),
                (await money.IncomeAndExpensesAsync(from, to, null, withSubcategories, default)).Where(flow => inThem.Any(transaction => transaction.Id == flow.TransactionId))
                    .Select(Text).Order(StringComparer.Ordinal));
            Assert.Equal(
                inThem.Select(transaction => $"{transaction.Id} {transaction.Date} {(!transaction.IsConverted ? null : type == TransactionType.Income ? transaction.IncomeAmountIn(withSubcategories) : transaction.SpentAmountIn(withSubcategories))}")
                    .Order(StringComparer.Ordinal),
                (await trends.AmountsAsync(from, to, type == TransactionType.Income ? TrendType.Income : TrendType.Expense, parent.Id, null, null, default))
                    .Select(amount => $"{amount.TransactionId} {amount.Date} {amount.Amount}").Order(StringComparer.Ordinal));
        }
    }

    private static IEnumerable<CategorySpent> SpentByCategory(IReadOnlyList<Transaction> expenses) =>
        expenses.SelectMany(expense => expense.Lines.Select(line => line.CategoryId)).Distinct().Select(category =>
        {
            var inIt = expenses.Where(expense => expense.Lines.Any(line => line.CategoryId == category)).ToList();
            return new CategorySpent(category, inIt.Sum(expense => expense.SpentAmountIn([category])), [.. inIt.Where(expense => !expense.IsConverted).Select(expense => expense.Id)]);
        });

    private static string Text(CategorySpent spent) => $"{spent.CategoryId} {spent.Spent:0.####} [{string.Join(",", spent.NotConverted.Order())}]";

    private static string Text(MoneyInOrOut flow) => $"{flow.TransactionId} {flow.Date} {flow.Income:0.####} {flow.Expenses:0.####} {flow.IsConverted}";

    private static string Text(Transaction transaction, IReadOnlyCollection<Guid> categoryIds) =>
        Text(new MoneyInOrOut(transaction.Id, transaction.Date, transaction.IncomeAmountIn(categoryIds), transaction.SpentAmountIn(categoryIds), transaction.IsConverted));

    private async Task<Guid> UserIdAsync(string email)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PfmDbContext>().Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
    }

    /// <summary>
    /// Expenses, refunds, splits, income, a transfer and a deleted expense in VND and in USD, in February (before any
    /// rate, so not converted), March and April. USD is quoted at 25,001 from 1 March and at 25,050 from 10 March.
    /// </summary>
    private async Task<RecordedData> RecordEveryKindAsync(Browser browser)
    {
        api.ExchangeRateSource.Quote(March, dong: 25001m);
        api.ExchangeRateSource.Quote(March.AddDays(9), dong: 25050m);
        await api.FetchExchangeRatesAsync();

        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "50000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "1000");

        await SpendAsync(browser, everyday, "Groceries", "500000", March.AddDays(1));
        await RefundAsync(browser, everyday, "Groceries", "30000", March.AddDays(2));
        await SplitAsync(browser, everyday, "150000", March.AddDays(3), ("Groceries", "100000"), ("Household", "50000"));
        await RecordAsync(browser, "income", everyday, "Salary", "30000000", March.AddDays(4));
        await TransferAsync(browser, everyday, savings, "1000000", March.AddDays(5));
        var deleted = await SpendAsync(browser, everyday, "Groceries", "999000", March.AddDays(6));
        (await browser.DeleteAsync($"/api/v1/transactions/{deleted}")).EnsureSuccessStatusCode();
        await SplitAsync(browser, dollars, "10", March.AddDays(1), ("Groceries", "3.33"), ("Coffee", "3.33"), ("Transport", "3.34"));
        await SplitAsync(browser, dollars, "1", March.AddDays(10), ("Coffee", "0.01"), ("Groceries", "0.99"));
        await RefundAsync(browser, dollars, "Groceries", "2.50", March.AddDays(11));

        await SpendAsync(browser, everyday, "Groceries", "40000", new DateOnly(2026, 2, 10));
        await SpendAsync(browser, dollars, "Groceries", "5", new DateOnly(2026, 2, 20));
        await SplitAsync(browser, dollars, "3", new DateOnly(2026, 2, 21), ("Groceries", "1"), ("Coffee", "2"));

        await SpendAsync(browser, everyday, "Groceries", "70000", new DateOnly(2026, 4, 2));
        await SplitAsync(browser, dollars, "7", new DateOnly(2026, 4, 3), ("Groceries", "1.11"), ("Coffee", "2.22"), ("Transport", "3.67"));

        return new RecordedData(await CategoryAsync(browser, "Groceries"));
    }

    private static async Task SetBudgetAsync(Browser browser, string month, Guid categoryId, string amount)
    {
        var response = await browser.PutAsync($"/api/v1/budgets/{month}/{categoryId}", new { amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(Browser browser, string path)
    {
        var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record RecordedData(Guid Groceries);

    private sealed record SpentBody(string Currency, string Amount, int NotConverted);

    private sealed record ListBody(ListTotalBody Total);

    private sealed record ListTotalBody(int Count, SpentBody? Spent);

    private sealed record BudgetBody(Guid CategoryId, string Spent);

    private sealed record MonthBody(List<BudgetBody> Items);

    private sealed record DrillDownBody(string Total, int NotConverted);

    private sealed record TrendBody(string Total, int NotConverted);

    private sealed record FlowBody(string Income, string Expenses, int NotConverted);

    private sealed record IncomeVsExpensesBody(FlowBody Total);

    private sealed record CategoryBody(Guid Id, string Name);

    private sealed record ActualBody(CategoryBody Category, string Spent);

    private sealed record BudgetVsActualBody(List<ActualBody> Items);

    private sealed record SpendingItemBody(CategoryBody Category, string Spent, int NotConverted);

    private sealed record SpendingBody(List<SpendingItemBody> Items);
}
