using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.ExchangeRates;

namespace Pfm.Infrastructure.ExchangeRates;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> rate)
    {
        rate.ToTable("ExchangeRates");

        // One rate per pair per day (spec section 3, rule 7). The key also serves the lookup of a pair's
        // latest rate on or before a date.
        rate.HasKey(row => new { row.BaseCurrency, row.QuoteCurrency, row.Date });
        rate.Property(row => row.BaseCurrency).HasConversion<string>().HasMaxLength(3);
        rate.Property(row => row.QuoteCurrency).HasConversion<string>().HasMaxLength(3);
        rate.Property(row => row.Rate).HasPrecision(28, ExchangeRate.Decimals);
    }
}
