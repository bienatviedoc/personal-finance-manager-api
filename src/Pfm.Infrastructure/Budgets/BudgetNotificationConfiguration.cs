using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Budgets;

namespace Pfm.Infrastructure.Budgets;

internal sealed class BudgetNotificationConfiguration : IEntityTypeConfiguration<BudgetNotification>
{
    public void Configure(EntityTypeBuilder<BudgetNotification> notification)
    {
        notification.ToTable("BudgetNotifications", table =>
            table.HasCheckConstraint("CK_BudgetNotifications_State_Warning_Or_Over", "\"State\" IN ('Warning', 'Over')"));
        notification.HasKey(row => row.Id);
        notification.Property(row => row.Id).ValueGeneratedNever();

        // The 1st of the budget month.
        notification.Property(row => row.Month).HasConversion(month => month.FirstDay, firstDay => BudgetMonth.Of(firstDay));
        notification.Property(row => row.State).HasConversion<string>().HasMaxLength(10);
        notification.Property(row => row.Budgeted).HasPrecision(19, 4);
        notification.Property(row => row.Spent).HasPrecision(19, 4);
        notification.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);
        notification.Ignore(row => row.PercentSpent);

        // At most one warning and one over per category a month (spec section 6), even when two requests raise it at once.
        notification.HasIndex(row => new { row.UserId, row.CategoryId, row.Month, row.State }).IsUnique();

        // The list, the newest first: an id is made from the moment it was raised.
        notification.HasIndex(row => new { row.UserId, row.Id });
    }
}
