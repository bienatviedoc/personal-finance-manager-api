using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> attachment)
    {
        attachment.ToTable("Attachments", table =>
            table.HasCheckConstraint("CK_Attachments_Size", $"\"Size\" > 0 AND \"Size\" <= {Attachment.MaxSize}"));
        attachment.HasKey(row => row.Id);
        attachment.Property(row => row.Id).ValueGeneratedNever();
        attachment.Property(row => row.StorageKey).HasMaxLength(200);
        attachment.Property(row => row.FileName).HasMaxLength(Attachment.MaxFileNameLength);
        attachment.Property(row => row.ContentType).HasMaxLength(100);

        // A file in storage has one attachment.
        attachment.HasIndex(row => row.StorageKey).IsUnique();

        // A transaction is deleted softly and keeps its attachments; nothing removes its row from under them.
        // Purging deleted transactions (spec rule 10) must delete their attachments first, files then rows:
        // restricted, the purge fails rather than leave files in storage that no row points at.
        attachment.HasOne<Transaction>().WithMany().HasForeignKey(row => row.TransactionId).OnDelete(DeleteBehavior.Restrict);

        // The attachments of one transaction, the first attached first (ids are in time order).
        attachment.HasIndex(row => new { row.UserId, row.TransactionId, row.Id });
    }
}
