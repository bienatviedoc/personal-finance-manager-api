namespace Pfm.Domain.Reports;

/// <summary>What one transaction adds to a trend, on its date.</summary>
/// <param name="Amount">In the base currency; null while the transaction is not converted, when it adds nothing.</param>
public sealed record TrendAmount(Guid TransactionId, DateOnly Date, decimal? Amount);

/// <summary>How a total changed from one period to the next (spec RPT-7, period-over-period change).</summary>
/// <param name="Amount">The later total minus the earlier one: negative when it went down.</param>
/// <param name="Percent">
/// <paramref name="Amount"/> as a percentage of the earlier total's size, to one decimal; null when the earlier total
/// is zero, which no change is a percentage of.
/// </param>
public sealed record PeriodChange(decimal Amount, decimal? Percent)
{
    public static PeriodChange Between(decimal before, decimal after)
    {
        var amount = after - before;
        return new PeriodChange(amount, before == 0 ? null : decimal.Round(amount * 100 / Math.Abs(before), 1, MidpointRounding.AwayFromZero));
    }
}

/// <summary>One month of a trend.</summary>
/// <param name="Period">
/// The calendar month, cut to the report period at either end; in a trend before a period that is not whole months,
/// the month at the same place in that period moved back by its days (<see cref="ReportPeriod.PreviousMonths"/>).
/// </param>
/// <param name="NotConverted">The transactions of the month left out of <paramref name="Total"/> because they are not converted.</param>
/// <param name="Change">How the total changed from the month before; null for the first month.</param>
public sealed record TrendMonth(ReportPeriod Period, decimal Total, IReadOnlySet<Guid> NotConverted, PeriodChange? Change);

/// <summary>
/// One category or one payee over time (spec RPT-7): what its transactions came to in each month of a report
/// period, and how each month changed from the one before.
/// </summary>
/// <param name="Months">Every month the period touches, in order; one with nothing in it at zero.</param>
/// <param name="NotConverted">The transactions left out of <paramref name="Total"/> because they are not converted.</param>
public sealed record Trend(ReportPeriod Period, IReadOnlyList<TrendMonth> Months, decimal Total, IReadOnlySet<Guid> NotConverted)
{
    /// <summary>Works out the trend over a period from what each transaction adds; those dated outside it are left out.</summary>
    public static Trend Of(ReportPeriod period, IEnumerable<TrendAmount> amounts)
    {
        ArgumentNullException.ThrowIfNull(period);
        return Over(period, period.Months, amounts);
    }

    /// <summary>
    /// Works out the trend over the period before <paramref name="period"/> (<see cref="ReportPeriod.Previous"/>), to
    /// compare with: one month for each of the period's, at the same place (<see cref="ReportPeriod.PreviousMonths"/>),
    /// so the two always pair up month by month. Those dated outside it are left out.
    /// </summary>
    public static Trend Before(ReportPeriod period, IEnumerable<TrendAmount> amounts)
    {
        ArgumentNullException.ThrowIfNull(period);
        return Over(period.Previous, period.PreviousMonths, amounts);
    }

    private static Trend Over(ReportPeriod period, IReadOnlyList<ReportPeriod> buckets, IEnumerable<TrendAmount> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        var all = amounts.Where(amount => period.Contains(amount.Date)).ToList();
        var months = new List<TrendMonth>();
        foreach (var month in buckets)
        {
            var inMonth = all.Where(amount => month.Contains(amount.Date)).ToList();
            var total = inMonth.Sum(amount => amount.Amount ?? 0);
            var change = months.Count == 0 ? null : PeriodChange.Between(months[^1].Total, total);
            months.Add(new TrendMonth(month, total, NotConvertedOf(inMonth), change));
        }

        return new Trend(period, months, months.Sum(month => month.Total), NotConvertedOf(all));
    }

    /// <summary>How the total changed from another trend's, the one over the period compared with.</summary>
    public PeriodChange ChangeFrom(Trend previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return PeriodChange.Between(previous.Total, Total);
    }

    private static HashSet<Guid> NotConvertedOf(IEnumerable<TrendAmount> amounts) =>
        [.. amounts.Where(amount => amount.Amount is null).Select(amount => amount.TransactionId)];
}
