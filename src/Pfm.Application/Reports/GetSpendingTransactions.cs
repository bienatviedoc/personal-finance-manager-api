using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// The last step of drilling down spending by category (spec RPT-2): the expenses that make up what was spent
/// in one category itself over a period, each with what it spent there, so they add up to that category's spent.
/// </summary>
/// <param name="AccountId">Only the expenses on this account; on every account when null.</param>
public sealed record GetSpendingTransactions(ReportPeriod Period, Guid CategoryId, Guid? AccountId = null);

/// <summary>The expenses in one category over a period.</summary>
/// <param name="Total">What their spent adds up to: the category's own spent in the report.</param>
/// <param name="NotConverted">How many of them are left out of <paramref name="Total"/> because they are not converted.</param>
/// <param name="Items">Newest first.</param>
public sealed record SpendingTransactionsDetails(
    ReportPeriod Period, Currency Currency, ReportCategoryDetails Category, decimal Total, int NotConverted, IReadOnlyList<CategoryExpense> Items);

public sealed class GetSpendingTransactionsHandler(ICategoryLookup categories, ICategorySpending spending, IBaseCurrency baseCurrency) : IQueryHandler
{
    /// <returns>Null when the category is not one of the user's.</returns>
    public async Task<SpendingTransactionsDetails?> HandleAsync(GetSpendingTransactions query, CancellationToken cancellationToken = default)
    {
        var category = (await categories.ListAsync(cancellationToken)).SingleOrDefault(category => category.Id == query.CategoryId);
        if (category is null)
        {
            return null;
        }

        var expenses = await spending.ExpensesInAsync(query.Period.From, query.Period.To, query.CategoryId, query.AccountId, cancellationToken);
        return new SpendingTransactionsDetails(
            query.Period, await baseCurrency.OfCurrentUserAsync(cancellationToken), ReportCategoryDetails.Of(category),
            expenses.Sum(expense => expense.Spent ?? 0), expenses.Count(expense => expense.Spent is null), expenses);
    }
}
