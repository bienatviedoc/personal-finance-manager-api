using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Name).HasMaxLength(Category.MaxNameLength);
        builder.Property(category => category.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(category => category.Icon).HasMaxLength(50);
        builder.Property(category => category.Color).HasConversion(color => color.Hex, hex => Color.Parse(hex)).HasMaxLength(7);

        // NO ACTION, checked at the end of the statement, so one statement can delete a parent with its
        // subcategories (as erasing a user's data will, #43).
        builder.HasOne<Category>().WithMany().HasForeignKey(category => category.ParentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(category => new { category.UserId, category.ParentId });
    }
}
