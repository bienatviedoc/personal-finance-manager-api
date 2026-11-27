using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Reports;
using Pfm.Domain;

namespace Pfm.Api.Reports;

/// <summary>A balance left in its own currency because no exchange rate exists to convert it.</summary>
public sealed record DashboardNotConvertedResponse(Currency Currency, string Balance);

/// <summary>What every account that is not archived holds, in the base currency.</summary>
/// <param name="Balance">A decimal string in the base currency, as the accounts list adds it up.</param>
/// <param name="NotConverted">What the balance leaves out, in each currency; empty when it is complete.</param>
public sealed record DashboardBalanceResponse(string Balance, IReadOnlyList<DashboardNotConvertedResponse> NotConverted)
{
    public static DashboardBalanceResponse Of(BaseCurrencyBalance balance) =>
        new(AmountJson.Text(balance.Balance), [.. balance.NotConverted.Select(amount => new DashboardNotConvertedResponse(amount.Currency, AmountJson.Text(amount.Balance)))]);
}

/// <summary>One of the categories that spent most this month.</summary>
/// <param name="Spent">What its expenses came to, minus refunds, with its subcategories'; a decimal string in the base currency.</param>
/// <param name="Share">Spent as a percentage of what every category spent, to one decimal; null when that is not above zero.</param>
/// <param name="NotConverted">How many of its expenses are left out of spent because they are not converted.</param>
public sealed record DashboardCategoryResponse(ReportCategoryResponse Category, string Spent, decimal? Share, int NotConverted)
{
    public static DashboardCategoryResponse Of(CategorySpendingDetails item) =>
        new(ReportCategoryResponse.From(item.Category), AmountJson.Text(item.Share.Spent), item.Share.Share, item.Share.NotConverted.Count);
}

/// <summary>A budget this month against what was spent against it.</summary>
/// <param name="PercentSpent">Spent as whole percent of budgeted, rounded down; null when budgeted is zero.</param>
/// <param name="State">Over above what was budgeted, a warning at or above the user's warning threshold, otherwise on track.</param>
public sealed record DashboardBudgetResponse(ReportCategoryResponse Category, string Budgeted, string Spent, string Remaining, int? PercentSpent, BudgetVsActualState State)
{
    public static DashboardBudgetResponse Of(BudgetedCategoryDetails item) =>
        new(ReportCategoryResponse.From(item.Category), AmountJson.Text(item.Budget.Budgeted), AmountJson.Text(item.Budget.Spent),
            AmountJson.Text(item.Budget.Remaining), item.Budget.PercentSpent, BudgetVsActualItemResponse.StateOf(item.Budget.State));
}

/// <summary>Whether a budget is being spent faster than the month is passing (spec BUD-6).</summary>
public enum DashboardBudgetPace
{
    OnPace = 1,
    AheadOfPace = 2,
}

/// <summary>This month's overall budget against every expense of the month (spec BUD-5).</summary>
/// <param name="PercentSpent">Spent as whole percent of budgeted, rounded down; null when budgeted is zero.</param>
/// <param name="State">Over above what was budgeted, a warning at or above the user's warning threshold, otherwise on track.</param>
/// <param name="Pace"><c>aheadOfPace</c> when a larger share of the budget is spent than of the month has passed, otherwise <c>onPace</c>.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
public sealed record DashboardOverallBudgetResponse(
    string Budgeted, string Spent, string Remaining, int? PercentSpent, BudgetVsActualState State, DashboardBudgetPace? Pace, int NotConverted)
{
    public static DashboardOverallBudgetResponse? Of(OverallBudgetedAndSpent? overall) =>
        overall is null
            ? null
            : new(AmountJson.Text(overall.Budgeted), AmountJson.Text(overall.Spent), AmountJson.Text(overall.Remaining), overall.PercentSpent,
                BudgetVsActualItemResponse.StateOf(overall.State), PaceOf(overall.Pace), overall.NotConverted);

    private static DashboardBudgetPace? PaceOf(ComparedBudgetPace? pace) => pace switch
    {
        null => null,
        ComparedBudgetPace.OnPace => DashboardBudgetPace.OnPace,
        ComparedBudgetPace.AheadOfPace => DashboardBudgetPace.AheadOfPace,
        _ => throw new ArgumentOutOfRangeException(nameof(pace), pace, null),
    };
}

/// <summary>This month's budgets added up, as budget vs actual adds them up, with how many are in each state.</summary>
/// <param name="PercentSpent">Spent as whole percent of budgeted, rounded down; null when nothing is budgeted.</param>
/// <param name="NotConverted">How many expenses are left out of spent because they are not converted.</param>
/// <param name="NeedingAttention">The budgets over, then those at warning, each by percentage spent, most first.</param>
/// <param name="Overall">This month's overall budget; null when it has none. The totals and counts are of the category budgets only.</param>
public sealed record DashboardBudgetsResponse(
    string Budgeted, string Spent, string Remaining, int? PercentSpent, int OnTrack, int Warning, int Over, int NotConverted,
    IReadOnlyList<DashboardBudgetResponse> NeedingAttention, DashboardOverallBudgetResponse? Overall)
{
    public static DashboardBudgetsResponse? Of(DashboardBudgetsDetails? budgets) =>
        budgets is null
            ? null
            : new(AmountJson.Text(budgets.Budgeted), AmountJson.Text(budgets.Spent), AmountJson.Text(budgets.Remaining), budgets.PercentSpent,
                budgets.OnTrack, budgets.Warning, budgets.Over, budgets.NotConverted, [.. budgets.NeedingAttention.Select(DashboardBudgetResponse.Of)],
                DashboardOverallBudgetResponse.Of(budgets.Overall));
}

/// <summary>The type of a transaction on the dashboard: it gives its amount its sign.</summary>
public enum DashboardTransactionType
{
    Income = 1,
    Expense = 2,
    TransferOut = 3,
    TransferIn = 4,
}

/// <summary>An account, named.</summary>
public sealed record DashboardAccountResponse(Guid Id, string Name)
{
    public static DashboardAccountResponse Of(DashboardAccountDetails account) => new(account.Id, account.Name);
}

/// <summary>One of the latest transactions.</summary>
/// <param name="Amount">A decimal string above zero in <paramref name="Currency"/>, its account's; the type gives the sign, except that a refund raises the balance.</param>
/// <param name="Category">The category of its first line, its only one unless it is split; null for a leg of a transfer.</param>
/// <param name="TransferId">The transfer a leg belongs to; null for income and expenses.</param>
public sealed record DashboardTransactionResponse(
    Guid Id, DateOnly Date, DashboardTransactionType Type, DashboardAccountResponse Account, string Amount, Currency Currency, ReportCategoryResponse? Category,
    string? Payee, bool IsRefund, bool IsSplit, Guid? TransferId)
{
    public static DashboardTransactionResponse Of(DashboardTransactionDetails transaction) =>
        new(transaction.Id, transaction.Date, TypeOf(transaction.Type), DashboardAccountResponse.Of(transaction.Account), AmountJson.Text(transaction.Amount),
            transaction.Currency, transaction.Category is { } category ? ReportCategoryResponse.From(category) : null, transaction.Payee,
            transaction.IsRefund, transaction.IsSplit, transaction.TransferId);

    internal static DashboardTransactionType TypeOf(ActivityType type) => type switch
    {
        ActivityType.Income => DashboardTransactionType.Income,
        ActivityType.Expense => DashboardTransactionType.Expense,
        ActivityType.TransferOut => DashboardTransactionType.TransferOut,
        ActivityType.TransferIn => DashboardTransactionType.TransferIn,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

/// <summary>An occurrence of a recurring rule still to come.</summary>
/// <param name="Type">Income or an expense.</param>
/// <param name="Amount">A decimal string above zero in <paramref name="Currency"/>, the rule's account's.</param>
/// <param name="IsReminder">The rule reminds: the occurrence is recorded only once the user confirms it. Otherwise the worker records it on its due date.</param>
public sealed record DashboardOccurrenceResponse(
    Guid RecurringRuleId, DateOnly DueDate, DashboardTransactionType Type, DashboardAccountResponse Account, string Amount, Currency Currency,
    ReportCategoryResponse Category, string? Payee, bool IsReminder)
{
    public static DashboardOccurrenceResponse Of(DashboardOccurrenceDetails occurrence) =>
        new(occurrence.RecurringRuleId, occurrence.DueDate, DashboardTransactionResponse.TypeOf(occurrence.Type), DashboardAccountResponse.Of(occurrence.Account),
            AmountJson.Text(occurrence.Amount), occurrence.Currency, ReportCategoryResponse.From(occurrence.Category), occurrence.Payee, occurrence.IsReminder);
}

/// <summary>The state of the user's money on one screen (spec RPT-1). Amounts are decimal strings in the base currency unless they say otherwise.</summary>
/// <param name="From">The first day of this month, in the user's time zone.</param>
/// <param name="To">The last day of this month.</param>
/// <param name="Currency">The user's base currency.</param>
/// <param name="HasAccounts">Whether the user has an account that is not archived; a new user has none yet, and is shown how to add one.</param>
/// <param name="ThisMonth">This month's income, expenses (minus refunds) and net, with the savings rate.</param>
/// <param name="Budgets">This month's budgets; null when none, category or overall, is set this month.</param>
/// <param name="TopCategories">The top-level categories that spent most this month, at most five, most first.</param>
/// <param name="RecentTransactions">The ten latest transactions, newest first, as the transaction list starts.</param>
/// <param name="Upcoming">The occurrences of recurring rules due in the next 30 days, at most ten, earliest first.</param>
public sealed record DashboardResponse(
    DateOnly From, DateOnly To, Currency Currency, bool HasAccounts, DashboardBalanceResponse Balance, IncomeAndExpensesResponse ThisMonth,
    DashboardBudgetsResponse? Budgets, IReadOnlyList<DashboardCategoryResponse> TopCategories, IReadOnlyList<DashboardTransactionResponse> RecentTransactions,
    IReadOnlyList<DashboardOccurrenceResponse> Upcoming)
{
    public static DashboardResponse Of(DashboardDetails dashboard) =>
        new(dashboard.Month.From, dashboard.Month.To, dashboard.Currency, dashboard.HasAccounts, DashboardBalanceResponse.Of(dashboard.Balance),
            IncomeAndExpensesResponse.Of(dashboard.ThisMonth), DashboardBudgetsResponse.Of(dashboard.Budgets),
            [.. dashboard.TopCategories.Select(DashboardCategoryResponse.Of)], [.. dashboard.RecentTransactions.Select(DashboardTransactionResponse.Of)],
            [.. dashboard.Upcoming.Select(DashboardOccurrenceResponse.Of)]);
}
