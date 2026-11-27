using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Imports;

namespace Pfm.Infrastructure.Imports;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    private static readonly JsonSerializerOptions MappingJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Configure(EntityTypeBuilder<ImportBatch> batch)
    {
        batch.ToTable("ImportBatches", table =>
        {
            table.HasCheckConstraint("CK_ImportBatches_Status", "\"Status\" IN ('Uploaded', 'Committing', 'Committed', 'Failed')");
            table.HasCheckConstraint("CK_ImportBatches_ImportedCount_Within_Selected", "\"ImportedCount\" >= 0 AND \"ImportedCount\" <= \"SelectedCount\"");

            // The file is kept only until its rows are imported: it can hold a full account number (spec ACC-8).
            table.HasCheckConstraint("CK_ImportBatches_File_Kept_Only_Until_Imported", "\"Content\" IS NULL OR \"Status\" IN ('Uploaded', 'Committing')");
        });
        batch.HasKey(row => row.Id);
        batch.Property(row => row.Id).ValueGeneratedNever();
        batch.Property(row => row.FileName).HasMaxLength(ImportBatch.MaxFileNameLength);
        batch.Property(row => row.Status).HasConversion<string>().HasMaxLength(20);
        batch.Property(row => row.Failure).HasConversion<string>().HasMaxLength(40);

        // How the file was read when it was committed, as the API writes a mapping: for the next import of the account to start from (spec IMP-2).
        batch.Property(row => row.Mapping)
            .HasColumnType("jsonb")
            .HasConversion(
                mapping => mapping == null ? null : JsonSerializer.Serialize(mapping, MappingJson),
                json => json == null ? null : JsonSerializer.Deserialize<StatementMapping>(json, MappingJson));

        batch.Property<List<int>>("_selectedRows")
            .HasColumnName(nameof(ImportBatch.SelectedRows))
            .Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (left, right) => left!.SequenceEqual(right!), rows => rows.Aggregate(0, HashCode.Combine), rows => rows.ToList()));
        batch.Ignore(row => row.SelectedRows);

        // PostgreSQL's own row version (xmin): of two workers importing the same rows, only the first saves them.
        batch.Property(row => row.RowVersion).IsRowVersion();

        batch.HasIndex(row => new { row.UserId, row.CreatedAt });

        // One batch at a time is committed into an account, so a statement committed twice before the worker gets to the
        // first finds the first's rows as likely duplicates once they are imported (spec IMP-3), never imports them twice.
        batch.HasIndex(row => row.AccountId).IsUnique().HasFilter("\"Status\" = 'Committing'").HasDatabaseName("IX_ImportBatches_AccountId_Committing");

        // The batches being committed, which the worker asks for every few seconds.
        batch.HasIndex(row => row.UserId).HasFilter("\"Status\" = 'Committing'").HasDatabaseName("IX_ImportBatches_UserId_Committing");
    }
}
