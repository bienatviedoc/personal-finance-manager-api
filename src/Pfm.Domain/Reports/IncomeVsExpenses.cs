namespace Pfm.Domain.Reports;

/// <summary>What one transaction brings to income and to expenses, in the base currency.</summary>
/// <param name="Income">Its converted amount when it is income; zero otherwise.</param>
/// <param name="Expenses">What it spent when it is an expense: negative for a refund; zero otherwise.</param>
/// <param name="IsConverted">False while it has no amount in the base currency, when it adds nothing and is counted as not converted.</param>
public sealed record TransactionFlow(Guid TransactionId, DateOnly Date, decimal Income, decimal Expenses, bool IsConverted);

/// <summary>Income against expenses over some dates (spec RPT-3).</summary>
/// <param name="Expenses">Spent: expenses minus refunds.</param>
/// <param name="NotConverted">The transactions left out because they are not converted, by id.</param>
public sealed record IncomeAndExpenses(decimal Income, decimal Expenses, IReadOnlySet<Guid> NotConverted)
{
    /// <summary>Income minus expenses: what was saved, negative when more went out than came in.</summary>
    public decimal Net => Income - Expenses;

    /// <summary>
    /// The savings rate: net as a percentage of income, to one decimal, negative when more went out than came in.
    /// None when income is not above zero, as a share of nothing says nothing.
    /// </summary>
    public decimal? SavingsRate => Income > 0 ? decimal.Round(Net * 100 / Income, 1, MidpointRounding.AwayFromZero) : null;

    public static IncomeAndExpenses Of(IEnumerable<TransactionFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(flows);
        var all = flows.ToList();
        return new IncomeAndExpenses(
            all.Sum(flow => flow.Income), all.Sum(flow => flow.Expenses), all.Where(flow => !flow.IsConverted).Select(flow => flow.TransactionId).ToHashSet());
    }
}

/// <summary>One month of the report.</summary>
/// <param name="Month">The first day of the calendar month its period starts in.</param>
/// <param name="Period">
/// The days of it the report covers: the whole month, unless the report period starts or ends inside it; in the
/// report before a period that is not whole months, the month at the same place moved back by the period's days
/// (<see cref="ReportPeriod.PreviousMonths"/>).
/// </param>
public sealed record MonthOfIncomeAndExpenses(DateOnly Month, ReportPeriod Period, IncomeAndExpenses Totals);

/// <summary>
/// Am I saving (spec RPT-3): income, expenses and net in each calendar month of a report period, with the
/// savings rate, and the same over the whole period.
/// </summary>
/// <param name="Months">Every month the period touches, in order, a month with nothing in it included.</param>
public sealed record IncomeVsExpenses(IReadOnlyList<MonthOfIncomeAndExpenses> Months, IncomeAndExpenses Total)
{
    /// <param name="flows">What each transaction brings; those dated outside the period are left out.</param>
    public static IncomeVsExpenses Of(ReportPeriod period, IReadOnlyCollection<TransactionFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(period);
        return Over(period, period.Months, flows);
    }

    /// <summary>
    /// Works out the report over the period before <paramref name="period"/> (<see cref="ReportPeriod.Previous"/>), to
    /// compare with: one month for each of the period's, at the same place (<see cref="ReportPeriod.PreviousMonths"/>),
    /// so the two always pair up month by month.
    /// </summary>
    /// <param name="flows">What each transaction brings; those dated outside the period before are left out.</param>
    public static IncomeVsExpenses Before(ReportPeriod period, IReadOnlyCollection<TransactionFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(period);
        return Over(period.Previous, period.PreviousMonths, flows);
    }

    private static IncomeVsExpenses Over(ReportPeriod period, IReadOnlyList<ReportPeriod> buckets, IReadOnlyCollection<TransactionFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(flows);
        var inPeriod = flows.Where(flow => period.Contains(flow.Date)).ToList();
        MonthOfIncomeAndExpenses[] months =
        [
            .. buckets.Select(days => new MonthOfIncomeAndExpenses(
                new DateOnly(days.From.Year, days.From.Month, 1), days, IncomeAndExpenses.Of(inPeriod.Where(flow => days.Contains(flow.Date))))),
        ];
        return new IncomeVsExpenses(months, IncomeAndExpenses.Of(inPeriod));
    }
}
