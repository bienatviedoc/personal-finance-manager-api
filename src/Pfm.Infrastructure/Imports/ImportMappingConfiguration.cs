using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Imports;

namespace Pfm.Infrastructure.Imports;

internal sealed class ImportMappingConfiguration : IEntityTypeConfiguration<ImportMapping>
{
    public void Configure(EntityTypeBuilder<ImportMapping> mapping)
    {
        mapping.ToTable("ImportMappings");
        mapping.HasKey(row => row.Id);
        mapping.Property(row => row.Id).ValueGeneratedNever();

        // Written as a batch keeps the mapping it was committed with: its enums by name.
        mapping.Property(row => row.Mapping)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, ImportBatchConfiguration.MappingJson),
                json => JsonSerializer.Deserialize<StatementMapping>(json, ImportBatchConfiguration.MappingJson)!);

        // One per account.
        mapping.HasIndex(row => row.AccountId).IsUnique();
    }
}
