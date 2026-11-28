using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pfm.Infrastructure.Transactions;

internal sealed class LineShareConfiguration : IEntityTypeConfiguration<LineShare>
{
    public void Configure(EntityTypeBuilder<LineShare> share)
    {
        share.HasNoKey().ToView(LineShare.View);
        share.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        share.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);
        share.Property(row => row.Amount).HasPrecision(19, 4);
        share.Property(row => row.BaseCurrencyAmount).HasPrecision(19, 4);
    }
}
