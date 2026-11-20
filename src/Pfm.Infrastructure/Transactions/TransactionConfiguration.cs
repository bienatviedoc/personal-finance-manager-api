using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    /// <summary>
    /// The key of the request that recorded the transaction, when it sent one. Not part of the transaction
    /// itself, so it is a column only.
    /// </summary>
    public const string IdempotencyKey = "IdempotencyKey";

    public void Configure(EntityTypeBuilder<Transaction> transaction)
    {
        transaction.ToTable("Transactions", table => table.HasCheckConstraint("CK_Transactions_Amount_Positive", "\"Amount\" > 0"));
        transaction.HasKey(row => row.Id);
        transaction.Property(row => row.Id).ValueGeneratedNever();
        transaction.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        transaction.Property(row => row.Amount).HasPrecision(19, 4);
        transaction.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);
        transaction.Property(row => row.Payee).HasMaxLength(Transaction.MaxPayeeLength);
        transaction.Property(row => row.Note).HasMaxLength(Transaction.MaxNoteLength);
        transaction.Property<string?>(IdempotencyKey).HasMaxLength(200);

        // PostgreSQL's own row version (xmin): it changes with every update, so a stale edit matches no row.
        transaction.Property(row => row.RowVersion).IsRowVersion();
        transaction.Ignore(row => row.IsDeleted);
        transaction.Ignore(row => row.SignedAmount);

        // A category with transactions is archived, never deleted.
        transaction.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // The list, newest first; and the sum of an account's transactions, which is its balance (spec section 10).
        transaction.HasIndex(row => new { row.UserId, row.Date, row.Id }).IsDescending(false, true, true);
        transaction.HasIndex(row => new { row.UserId, row.AccountId, row.Date });

        // A retried request cannot record its transaction twice. Rows without a key never collide.
        transaction.HasIndex(nameof(Transaction.UserId), IdempotencyKey).IsUnique();
    }
}
