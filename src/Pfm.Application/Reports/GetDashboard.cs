using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// The dashboard (spec RPT-1): the state of the user's money on one screen. The total balance, and this month's
/// income, expenses and net, budget status and top categories, as the accounts screen and the reports work them out
/// for this month, with the latest transactions and the recurring rules' occurrences still to come.
/// </summary>
public sealed record GetDashboard
{
    /// <summary>How many categories spending ranks.</summary>
    public const int TopCategoryCount = 5;

    /// <summary>How many of the latest transactions it lists.</summary>
    public const int RecentTransactionCount = 10;

    /// <summary>How many days ahead it looks for occurrences, as the recurring screen does.</summary>
    public const int UpcomingDays = 30;

    /// <summary>How many of those occurrences it lists at most, the earliest first.</summary>
    public const int UpcomingCount = 10;
}

/// <summary>The dashboard, every amount in the base currency unless it says otherwise.</summary>
/// <param name="Month">This month, whole, in the user's time zone.</param>
/// <param name="HasAccounts">Whether the user has an account that is not archived; a new user has none yet.</param>
/// <param name="Balance">The balance of every account that is not archived, as the accounts screen adds it up.</param>
/// <param name="ThisMonth">This month's income, expenses and net, as income vs expenses works them out.</param>
/// <param name="Budgets">This month's budgets against what was spent; null when no budget is set this month.</param>
/// <param name="TopCategories">The top-level categories that spent most this month, at most five, most first, as spending by category ranks them.</param>
public sealed record DashboardDetails(
    ReportPeriod Month, Currency Currency, bool HasAccounts, BaseCurrencyBalance Balance, IncomeAndExpenses ThisMonth, DashboardBudgets? Budgets,
    IReadOnlyList<CategorySpendingDetails> TopCategories, IReadOnlyList<DashboardTransaction> RecentTransactions,
    IReadOnlyList<DashboardOccurrence> Upcoming);

/// <summary>This month's budgets, added up, with the categories that need attention.</summary>
/// <param name="PercentSpent">Spent as whole percent of budgeted, rounded down; null when nothing is budgeted.</param>
/// <param name="OnTrack">How many budgets are on track.</param>
/// <param name="Warning">How many are at or above the warning threshold and not over.</param>
/// <param name="Over">How many are over budget.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted, each counted once.</param>
/// <param name="NeedingAttention">The budgets over, then those at warning, each by percentage spent, most first.</param>
public sealed record DashboardBudgets(
    decimal Budgeted, decimal Spent, decimal Remaining, int? PercentSpent, int OnTrack, int Warning, int Over, int NotConverted,
    IReadOnlyList<BudgetedCategoryDetails> NeedingAttention);

/// <summary>An account as the dashboard names it.</summary>
public sealed record DashboardAccount(Guid Id, string Name);

/// <summary>One of the latest transactions, with its account and category named.</summary>
/// <param name="Amount">Greater than zero, in <paramref name="Currency"/>, its account's currency; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="Category">The category of its first line; null for a leg of a transfer.</param>
public sealed record DashboardTransaction(
    Guid Id, DateOnly Date, TransactionKind Type, DashboardAccount Account, decimal Amount, Currency Currency, ReportCategoryDetails? Category,
    string? Payee, bool IsRefund, bool IsSplit, Guid? TransferId);

/// <summary>One occurrence of a recurring rule still to come, with its account and category named.</summary>
/// <param name="Amount">Greater than zero, in <paramref name="Currency"/>, the rule's account's currency.</param>
public sealed record DashboardOccurrence(
    Guid RecurringRuleId, DateOnly DueDate, TransactionKind Type, DashboardAccount Account, decimal Amount, Currency Currency,
    ReportCategoryDetails Category, string? Payee, bool IsReminder);

public sealed class GetDashboardHandler(
    IUserCalendar calendar, IBaseCurrency baseCurrency, IAccountLookup accounts, IAccountTotals totals, ICategoryLookup categories,
    IRecentActivity activity, GetIncomeVsExpensesHandler incomeVsExpenses, GetSpendingByCategoryHandler spendingByCategory,
    GetBudgetVsActualHandler budgetVsActual) : IQueryHandler
{
    public async Task<DashboardDetails> HandleAsync(GetDashboard query, CancellationToken cancellationToken = default)
    {
        var today = await calendar.TodayAsync(cancellationToken);
        var month = new ReportPeriod(today, today).WholeMonths;
        var filter = new ReportFilter(month);

        var allAccounts = (await accounts.ListAsync(cancellationToken)).ToDictionary(account => account.Id);
        var allCategories = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);
        DashboardAccount AccountOf(Guid id) => new(id, allAccounts.TryGetValue(id, out var account) ? account.Name : "");
        ReportCategoryDetails? CategoryOf(Guid? id) => id is { } categoryId && allCategories.TryGetValue(categoryId, out var category) ? ReportCategoryDetails.Of(category) : null;

        var income = await incomeVsExpenses.HandleAsync(new GetIncomeVsExpenses(filter), cancellationToken);
        var spending = await spendingByCategory.HandleAsync(new GetSpendingByCategory(filter), cancellationToken)
            ?? throw new InvalidOperationException("Spending by category without a category to break down is always answered.");
        var budgets = await budgetVsActual.HandleAsync(new GetBudgetVsActual(filter), cancellationToken);

        var recent = (await activity.LatestAsync(GetDashboard.RecentTransactionCount, cancellationToken))
            .Select(transaction => new DashboardTransaction(
                transaction.Id, transaction.Date, transaction.Type, AccountOf(transaction.AccountId), transaction.Amount, transaction.Currency,
                CategoryOf(transaction.CategoryId), transaction.Payee, transaction.IsRefund, transaction.IsSplit, transaction.TransferId))
            .ToList();
        var upcoming = (await activity.UpcomingAsync(GetDashboard.UpcomingDays, cancellationToken))
            .Take(GetDashboard.UpcomingCount)
            .Select(occurrence => new DashboardOccurrence(
                occurrence.RecurringRuleId, occurrence.DueDate, occurrence.Type, AccountOf(occurrence.AccountId), occurrence.Amount, occurrence.Currency,
                CategoryOf(occurrence.CategoryId)!, occurrence.Payee, occurrence.IsReminder))
            .ToList();

        return new DashboardDetails(
            month, await baseCurrency.OfCurrentUserAsync(cancellationToken), allAccounts.Values.Any(account => !account.IsArchived),
            await totals.InBaseCurrencyAsync(cancellationToken), income.Report.Total, BudgetsOf(budgets),
            [.. spending.Items.Take(GetDashboard.TopCategoryCount)], recent, upcoming);
    }

    private static DashboardBudgets? BudgetsOf(BudgetVsActualDetails report)
    {
        if (report.Items.Count == 0)
        {
            return null;
        }

        var totals = report.Totals;
        int Counted(ComparedBudgetState state) => report.Items.Count(item => item.Budget.State == state);
        var needingAttention = report.Items
            .Where(item => item.Budget.State is ComparedBudgetState.Over or ComparedBudgetState.Warning)
            .OrderByDescending(item => item.Budget.State)
            .ThenByDescending(item => item.Budget.PercentSpent)
            .ThenBy(item => item.Category.Name, NameOrder.Comparer)
            .ThenBy(item => item.Category.Id)
            .ToList();
        int? percentSpent = totals.Budgeted > 0 ? (int)decimal.Floor(totals.Spent * 100 / totals.Budgeted) : null;
        return new DashboardBudgets(
            totals.Budgeted, totals.Spent, totals.Remaining, percentSpent, Counted(ComparedBudgetState.OnTrack), Counted(ComparedBudgetState.Warning),
            Counted(ComparedBudgetState.Over), totals.NotConverted, needingAttention);
    }
}
