using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Budgets;

namespace Pfm.Infrastructure.Budgets;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    /// <summary>
    /// The kind of category a budget takes: always an expense one. A column only, worked out by the database:
    /// it is there for <c>ModuleReferences.BudgetCategoryIsAnExpenseCategory</c> to point at the category with.
    /// </summary>
    public const string CategoryKind = "CategoryKind";

    public void Configure(EntityTypeBuilder<Budget> budget)
    {
        budget.ToTable("Budgets", table => table.HasCheckConstraint("CK_Budgets_Amount_Not_Negative", "\"Amount\" >= 0"));
        budget.HasKey(row => row.Id);
        budget.Property(row => row.Id).ValueGeneratedNever();

        // The 1st of the budget month.
        budget.Property(row => row.Month).HasConversion(month => month.FirstDay, firstDay => BudgetMonth.Of(firstDay));
        budget.Property(row => row.Amount).HasPrecision(19, 4);
        budget.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);
        budget.Property<string>(CategoryKind).HasMaxLength(10).HasComputedColumnSql("'Expense'", stored: true);

        // PostgreSQL's own row version (xmin): it changes with every update, so a stale change matches no row.
        budget.Property(row => row.RowVersion).IsRowVersion();

        // A category has one budget a month (spec section 10), and a month is read with all of its budgets.
        budget.HasIndex(row => new { row.UserId, row.CategoryId, row.Month }).IsUnique();
        budget.HasIndex(row => new { row.UserId, row.Month });
    }
}
