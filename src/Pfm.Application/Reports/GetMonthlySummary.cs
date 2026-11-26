using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// The monthly summary (spec section 7, common behaviour: export): one document of a month, as a PDF file, with
/// what the reports say about it: income, expenses, net and savings rate (RPT-3), spending by category (RPT-2)
/// and budget vs actual (RPT-6).
/// </summary>
/// <param name="Month">The whole calendar month summed up.</param>
/// <param name="Locale">The language it is written in, and how its amounts are; the user's own when null, English when they have none.</param>
public sealed record GetMonthlySummary(ReportPeriod Month, Locale? Locale = null);

/// <summary>The monthly summary as a file to download.</summary>
public sealed record MonthlySummaryFile(string FileName, byte[] Content);

public sealed class GetMonthlySummaryHandler(
    GetIncomeVsExpensesHandler incomeVsExpenses, GetSpendingByCategoryHandler spendingByCategory, GetBudgetVsActualHandler budgetVsActual,
    IUserLocale userLocale, IPdfWriter pdf) : IQueryHandler
{
    public async Task<MonthlySummaryFile> HandleAsync(GetMonthlySummary query, CancellationToken cancellationToken = default)
    {
        var filter = new ReportFilter(query.Month);
        var locale = query.Locale ?? await userLocale.OfCurrentUserAsync(cancellationToken) ?? Locale.En;
        var income = await incomeVsExpenses.HandleAsync(new GetIncomeVsExpenses(filter), cancellationToken);
        // Top level: each category stands for itself and its subcategories, so the shares add up to the whole.
        var spending = await spendingByCategory.HandleAsync(new GetSpendingByCategory(filter), cancellationToken)
            ?? throw new InvalidOperationException("Spending by category without a category to break down is always answered.");
        var budgets = await budgetVsActual.HandleAsync(new GetBudgetVsActual(filter), cancellationToken);

        var document = new MonthlySummaryDocument(MonthlySummaryText.In(locale), locale, income.Currency);
        var month = DateText.Of(query.Month.From)[..7];
        return new MonthlySummaryFile($"monthly-summary-{month}.pdf", pdf.Write(document.Of(query.Month, income, spending, budgets)));
    }
}

/// <summary>What the monthly summary says, written in one locale with amounts in one currency.</summary>
internal sealed class MonthlySummaryDocument(MonthlySummaryText text, Locale locale, Currency currency)
{
    private const string None = "—";

    public PrintedDocument Of(ReportPeriod month, IncomeVsExpensesDetails income, SpendingByCategoryDetails spending, BudgetVsActualDetails budgets) =>
        new(text.Title, text.Month(month.From), [IncomeAndExpenses(income), Spending(spending), Budgets(budgets)]);

    private PrintedSection IncomeAndExpenses(IncomeVsExpensesDetails report)
    {
        var total = report.Report.Total;
        var figures = new PrintedTable(
            null,
            [new PrintedColumn("", IsFigure: false, 0.5), new PrintedColumn("", IsFigure: true, 0.5)],
            [
                [text.Income, Amount(total.Income)],
                [text.Expenses, Amount(total.Expenses)],
                [text.Net, Amount(total.Net)],
                [text.SavingsRate, total.SavingsRate is { } rate ? locale.WritePercent(rate) : None],
            ]);
        var notConverted = total.NotConverted.Count;
        return new PrintedSection(text.IncomeVsExpenses, [figures], notConverted > 0 ? [text.NotConverted(notConverted)] : []);
    }

    private PrintedSection Spending(SpendingByCategoryDetails report)
    {
        if (report.Items.Count == 0)
        {
            return new PrintedSection(text.SpendingByCategory, [], [text.NothingSpent]);
        }

        var table = new PrintedTable(
            null,
            [new PrintedColumn(text.Category, IsFigure: false, 0.5), new PrintedColumn(text.Spent, IsFigure: true, 0.3), new PrintedColumn(text.Share, IsFigure: true, 0.2)],
            [.. report.Items.Select(item => (IReadOnlyList<string>)[item.Category.Name, Amount(item.Share.Spent), Percent(item.Share.Share)])],
            [text.TotalSpent, Amount(report.Total.Total), ""]);
        return new PrintedSection(text.SpendingByCategory, [table], []);
    }

    private PrintedSection Budgets(BudgetVsActualDetails report)
    {
        var tables = new List<PrintedTable>();
        if (report.Items.Count > 0)
        {
            tables.Add(new PrintedTable(
                null,
                [
                    new PrintedColumn(text.Category, IsFigure: false, 0.28), new PrintedColumn(text.Budgeted, IsFigure: true, 0.18),
                    new PrintedColumn(text.Spent, IsFigure: true, 0.18), new PrintedColumn(text.Remaining, IsFigure: true, 0.18),
                    new PrintedColumn(text.State, IsFigure: false, 0.18),
                ],
                [.. report.Items.Select(item => (IReadOnlyList<string>)
                    [item.Category.Name, Amount(item.Budget.Budgeted), Amount(item.Budget.Spent), Amount(item.Budget.Remaining), text.StateOf(item.Budget.State)])],
                [text.Total, Amount(report.Totals.Budgeted), Amount(report.Totals.Spent), Amount(report.Totals.Remaining), ""]));
        }

        if (report.Unbudgeted.Count > 0)
        {
            tables.Add(new PrintedTable(
                text.Unbudgeted,
                [new PrintedColumn(text.Category, IsFigure: false, 0.5), new PrintedColumn(text.Spent, IsFigure: true, 0.3), new PrintedColumn("", IsFigure: false, 0.2)],
                [.. report.Unbudgeted.Select(item => (IReadOnlyList<string>)[item.Category.Name, Amount(item.Spending.Spent), ""])]));
        }

        return new PrintedSection(text.BudgetVsActual, tables, report.Items.Count == 0 ? [text.NoBudgets] : []);
    }

    private string Amount(decimal amount) => locale.WriteAmount(amount, currency);

    private string Percent(decimal? percent) => percent is { } value ? locale.WritePercent(value) : None;
}

/// <summary>The words of the monthly summary in one locale, as the SPA says them on its reports.</summary>
internal sealed record MonthlySummaryText(
    string Title, Func<DateOnly, string> Month, string IncomeVsExpenses, string Income, string Expenses, string Net, string SavingsRate,
    Func<int, string> NotConverted, string SpendingByCategory, string Category, string Spent, string Share, string TotalSpent, string NothingSpent,
    string BudgetVsActual, string Budgeted, string Remaining, string State, string Total, string OnTrack, string Warning, string Over, string NoBudgets,
    string Unbudgeted)
{
    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public static MonthlySummaryText In(Locale locale) => locale switch
    {
        Locale.En => English,
        Locale.Vi => Vietnamese,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "No monthly summary in this locale."),
    };

    public string StateOf(ComparedBudgetState state) => state switch
    {
        ComparedBudgetState.OnTrack => OnTrack,
        ComparedBudgetState.Warning => Warning,
        ComparedBudgetState.Over => Over,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static readonly MonthlySummaryText English = new(
        "Monthly summary", month => $"{EnglishMonths[month.Month - 1]} {month.Year}", "Income vs expenses", "Income", "Expenses", "Net", "Savings rate",
        count => count == 1
            ? "1 amount is not converted into your base currency yet and is left out."
            : $"{count} amounts are not converted into your base currency yet and are left out.",
        "Spending by category", "Category", "Spent", "Share", "Total spent", "Nothing was spent in this month.",
        "Budget vs actual", "Budgeted", "Remaining", "State", "Total", "On track", "Warning", "Over budget", "No budget was set in this month.",
        "Spent with no budget");

    private static readonly MonthlySummaryText Vietnamese = new(
        "Tổng kết tháng", month => $"tháng {month.Month} năm {month.Year}", "Thu nhập và chi tiêu", "Thu nhập", "Chi tiêu", "Chênh lệch", "Tỷ lệ tiết kiệm",
        count => $"{count} khoản chưa được quy đổi sang tiền tệ cơ sở và không được tính.",
        "Chi tiêu theo danh mục", "Danh mục", "Đã chi", "Tỷ lệ", "Tổng chi", "Không có khoản chi nào trong tháng này.",
        "Ngân sách so với thực tế", "Ngân sách", "Còn lại", "Trạng thái", "Tổng", "Đúng kế hoạch", "Cảnh báo", "Vượt ngân sách", "Chưa đặt ngân sách nào trong tháng này.",
        "Đã chi không có ngân sách");
}
