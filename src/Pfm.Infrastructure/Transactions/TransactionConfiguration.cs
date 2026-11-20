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

    /// <summary>
    /// A fingerprint (SHA-256, in hex) of what the request with <see cref="IdempotencyKey"/> asked for, which
    /// tells its retry from another request using the key again. A column only, like the key.
    /// </summary>
    public const string IdempotencyFingerprint = "IdempotencyFingerprint";

    public void Configure(EntityTypeBuilder<Transaction> transaction)
    {
        transaction.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Amount_Positive", "\"Amount\" > 0");

            // Converted with its rate and the rate's day, or not converted and none of the three.
            table.HasCheckConstraint(
                "CK_Transactions_Converted_Or_Not",
                "(\"BaseCurrencyAmount\" IS NULL) = (\"ExchangeRate\" IS NULL) AND (\"BaseCurrencyAmount\" IS NULL) = (\"ExchangeRateDate\" IS NULL)");
        });
        transaction.HasKey(row => row.Id);
        transaction.Property(row => row.Id).ValueGeneratedNever();
        transaction.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        transaction.Property(row => row.Amount).HasPrecision(19, 4);
        transaction.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);
        // Null in the domain only until the transaction is first saved, which converts it or flags it not converted.
        transaction.Property(row => row.BaseCurrency).HasConversion<string>().HasMaxLength(3).IsRequired();
        transaction.Property(row => row.BaseCurrencyAmount).HasPrecision(19, 4);
        transaction.Property(row => row.ExchangeRate).HasPrecision(28, Domain.ExchangeRates.ExchangeRate.Decimals);
        transaction.Property(row => row.Payee).HasMaxLength(Transaction.MaxPayeeLength);
        transaction.Property(row => row.Note).HasMaxLength(Transaction.MaxNoteLength);
        transaction.Property<string?>(IdempotencyKey).HasMaxLength(200);
        transaction.Property<string?>(IdempotencyFingerprint).HasMaxLength(64);

        // PostgreSQL's own row version (xmin): it changes with every update, so a stale edit matches no row.
        transaction.Property(row => row.RowVersion).IsRowVersion();
        transaction.Ignore(row => row.IsDeleted);
        transaction.Ignore(row => row.SignedAmount);
        transaction.Ignore(row => row.IsConverted);

        // A category with transactions is archived, never deleted.
        transaction.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // The list, newest first; and the sum of an account's transactions, which is its balance (spec section 10).
        transaction.HasIndex(row => new { row.UserId, row.Date, row.Id }).IsDescending(false, true, true);
        transaction.HasIndex(row => new { row.UserId, row.AccountId, row.Date });

        // The transactions waiting for an exchange rate, which a fetch of new rates converts.
        transaction.HasIndex(row => row.UserId).HasFilter("\"BaseCurrencyAmount\" IS NULL").HasDatabaseName("IX_Transactions_UserId_NotConverted");

        // A retried request cannot record its transaction twice. Rows without a key never collide.
        transaction.HasIndex(nameof(Transaction.UserId), IdempotencyKey).IsUnique();
    }
}
