using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class RecurringRuleConfiguration : IEntityTypeConfiguration<RecurringRule>
{
    public void Configure(EntityTypeBuilder<RecurringRule> rule)
    {
        rule.ToTable("RecurringRules", table =>
        {
            table.HasCheckConstraint("CK_RecurringRules_Amount_Positive", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_RecurringRules_Type_IncomeOrExpense", "\"Type\" IN ('Income', 'Expense')");
            table.HasCheckConstraint("CK_RecurringRules_Interval_Positive", "\"Interval\" >= 1");
            table.HasCheckConstraint("CK_RecurringRules_Mode_AutoPostOrRemind", "\"Mode\" IN ('AutoPost', 'Remind')");
            table.HasCheckConstraint("CK_RecurringRules_Ends_After_It_Starts", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
        });
        rule.HasKey(row => row.Id);
        rule.Property(row => row.Id).ValueGeneratedNever();
        rule.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        rule.Property(row => row.Amount).HasPrecision(19, 4);
        rule.Property(row => row.Payee).HasMaxLength(Transaction.MaxPayeeLength);
        rule.Property(row => row.Note).HasMaxLength(Transaction.MaxNoteLength);
        rule.Property(row => row.Frequency).HasConversion<string>().HasMaxLength(20);
        rule.Property(row => row.Mode).HasConversion<string>().HasMaxLength(20);

        // PostgreSQL's own row version (xmin): of two posts of one occurrence, only the first moves the rule on.
        rule.Property(row => row.RowVersion).IsRowVersion();
        rule.Ignore(row => row.Schedule);

        // A category a rule uses is archived, never deleted.
        rule.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // A transaction links back to the rule that posted it for as long as the rule exists.
        rule.HasMany<Transaction>().WithOne().HasForeignKey(transaction => transaction.RecurringRuleId).OnDelete(DeleteBehavior.SetNull);

        rule.HasIndex(row => row.UserId);

        // The rules with an occurrence to post, which the worker asks for every few minutes.
        rule.HasIndex(row => row.NextRunDate).HasFilter("\"NextRunDate\" IS NOT NULL AND NOT \"IsPaused\" AND \"Mode\" = 'AutoPost'").HasDatabaseName("IX_RecurringRules_NextRunDate_Due");
    }
}
