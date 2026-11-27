using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Identity;

namespace Pfm.Infrastructure.Identity;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>
    /// The trigger by which the database refuses to change an audit entry, or delete one while its user's row is there.
    /// It is created by the migration that adds the table, and narrowed to that by <c>AuditEntriesGoWithTheirUser</c>.
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

        // Erasing a user deletes their row last; their entries go with it, and by no other route (see the trigger).
        entry.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);

        // The log, the newest first: an id is made from the moment it was recorded.
        entry.HasIndex(row => new { row.UserId, row.Id });
    }
}
