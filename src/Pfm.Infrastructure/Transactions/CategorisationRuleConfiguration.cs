using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class CategorisationRuleConfiguration : IEntityTypeConfiguration<CategorisationRule>
{
    public void Configure(EntityTypeBuilder<CategorisationRule> rule)
    {
        rule.ToTable("CategorisationRules", table =>
        {
            table.HasCheckConstraint("CK_CategorisationRules_MatchType", "\"MatchType\" IN ('Contains', 'StartsWith', 'Exactly')");
            table.HasCheckConstraint("CK_CategorisationRules_Priority_Not_Negative", "\"Priority\" >= 0");
        });
        rule.HasKey(row => row.Id);
        rule.Property(row => row.Id).ValueGeneratedNever();
        rule.Property(row => row.MatchText).HasMaxLength(CategorisationRule.MaxMatchTextLength);
        rule.Property(row => row.MatchType).HasConversion<string>().HasMaxLength(20);

        // PostgreSQL's own row version (xmin): it changes with every update, so a stale edit or reorder matches no row.
        rule.Property(row => row.RowVersion).IsRowVersion();

        // A category a rule uses is archived, never deleted; a rule on an archived one categorises nothing.
        rule.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

        rule.HasIndex(row => row.UserId);
    }
}
