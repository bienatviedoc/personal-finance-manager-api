using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Reports;

namespace Pfm.Application.Reports;

/// <summary>
/// What every report takes (spec section 7): a period, an account filter and a category filter. An id the user
/// has nothing under lets nothing through, so the report is empty rather than refused.
/// </summary>
/// <param name="AccountId">Only what happened on this account counts; every account when null.</param>
/// <param name="CategoryId">Only what is in this category, and in its subcategories when it has any, counts; every category when null.</param>
/// <param name="Compare">Whether the report is also worked out for the period before (<see cref="ReportPeriod.Previous"/>), to compare with.</param>
public sealed record ReportFilter(ReportPeriod Period, Guid? AccountId = null, Guid? CategoryId = null, bool Compare = false)
{
    /// <summary>The period compared with; null when no comparison was asked for.</summary>
    public ReportPeriod? ComparedWith => Compare ? Period.Previous : null;
}

/// <summary>A category as a report shows it, also when it has since been archived.</summary>
/// <param name="ParentId">The top-level category it sits under; null for a top-level category.</param>
public sealed record ReportCategoryDetails(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived)
{
    public static ReportCategoryDetails Of(CategorySummary category) =>
        new(category.Id, category.ParentId, category.Name, category.Icon, category.Color, category.IsArchived);
}
