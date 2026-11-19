using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Identity;

namespace Pfm.Infrastructure.Transactions;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.Property(category => category.Name).HasMaxLength(100);
        builder.Property(category => category.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(category => category.Icon).HasMaxLength(50);
        builder.Property(category => category.Color).HasMaxLength(7);

        builder.HasOne<User>().WithMany().HasForeignKey(category => category.UserId).OnDelete(DeleteBehavior.Cascade);
        // NO ACTION, checked at the end of the statement, so deleting a user (cascading to every category) can
        // remove parents and subcategories together.
        builder.HasOne<Category>().WithMany().HasForeignKey(category => category.ParentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(category => new { category.UserId, category.ParentId });
    }
}
