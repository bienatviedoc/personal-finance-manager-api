using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Identity;

namespace Pfm.Infrastructure.Identity;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>
    /// The trigger by which the database refuses to change an audit entry, or delete one of a user who is not deactivated
    /// (being erased). It is created by the migration that adds the table.
    /// </summary>
    public const string AppendOnlyTrigger = "TR_AuditEntries_AppendOnly";

    public void Configure(EntityTypeBuilder<AuditEntry> entry)
    {
        entry.ToTable("AuditEntries");
        entry.HasKey(row => row.Id);
        entry.Property(row => row.Id).ValueGeneratedNever();
        entry.Property(row => row.Action).HasConversion<string>().HasMaxLength(20);
        entry.Property(row => row.EntityType).HasConversion<string>().HasMaxLength(20);
        entry.Property(row => row.Summary).HasMaxLength(AuditEntry.MaxSummaryLength);

        // The log, the newest first: an id is made from the moment it was recorded.
        entry.HasIndex(row => new { row.UserId, row.Id });
    }
}
