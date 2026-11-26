namespace Pfm.Domain.Reports;

/// <summary>How often cash flow shows the running balance over a report period (spec RPT-4).</summary>
public enum BalanceInterval
{
    /// <summary>The balance at the end of every day.</summary>
    Day = 1,

    /// <summary>The balance at the end of every week, Monday to Sunday, and at the end of the period.</summary>
    Week = 2,
}
