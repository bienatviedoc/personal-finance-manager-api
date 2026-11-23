using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Application.Transactions;
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

    /// <summary>
    /// The kind of category the transaction's type takes, worked out by the database from the type. A column
    /// only: it is there for <see cref="CategoryKindIsItsCategorys"/> to point at the category with.
    /// </summary>
    public const string CategoryKind = "CategoryKind";

    /// <summary>
    /// A transaction's category and the kind its type takes are a category and that category's kind. So the
    /// database refuses a transaction recorded on a category that has just changed kind, and a change of kind
    /// on a category a transaction has just been recorded on, whichever of the two requests saves second.
    /// Added by its migration alone, like the two in <c>ModuleReferences</c>.
    /// </summary>
    public const string CategoryKindIsItsCategorys = "FK_Transactions_Categories_CategoryId_CategoryKind";

    public void Configure(EntityTypeBuilder<Transaction> transaction)
    {
        transaction.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Amount_Positive", "\"Amount\" > 0");

            // Only an expense can be a refund.
            table.HasCheckConstraint("CK_Transactions_Refund_Is_Expense", "NOT \"IsRefund\" OR \"Type\" = 'Expense'");

            // Converted with its rate and the rate's day, or not converted and none of the three.
            table.HasCheckConstraint(
                "CK_Transactions_Converted_Or_Not",
                "(\"BaseCurrencyAmount\" IS NULL) = (\"ExchangeRate\" IS NULL) AND (\"BaseCurrencyAmount\" IS NULL) = (\"ExchangeRateDate\" IS NULL)");

            // The legs of a transfer, and only they, share a transfer id (decision record 0001); a leg is
            // neither income nor spending, so it has no category.
            table.HasCheckConstraint("CK_Transactions_Transfer_Leg_Has_TransferId", "(\"Type\" IN ('TransferOut', 'TransferIn')) = (\"TransferId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_Transactions_Transfer_Leg_Has_No_Category", "\"TransferId\" IS NULL OR \"CategoryId\" IS NULL");
        });
        transaction.HasKey(row => row.Id);
        transaction.Property(row => row.Id).ValueGeneratedNever();
        transaction.Property(row => row.Type).HasConversion<string>().HasMaxLength(20);
        transaction.Property(row => row.Amount).HasPrecision(19, 4);
        transaction.Property(row => row.Currency).HasConversion<string>().HasMaxLength(3);

        // Null for a type that takes no category of either kind, which the foreign key then leaves alone.
        transaction.Property<string?>(CategoryKind)
            .HasMaxLength(10)
            .HasComputedColumnSql("""CASE "Type" WHEN 'Income' THEN 'Income' WHEN 'Expense' THEN 'Expense' END""", stored: true);

        // The conversion is columns of the transaction. Null in the domain only until the transaction is first
        // saved, which converts it or flags it not converted, so the base currency is always there.
        transaction.ComplexProperty(row => row.Conversion, conversion =>
        {
            conversion.IsRequired();
            conversion.Property(converted => converted.BaseCurrency).HasColumnName("BaseCurrency").HasConversion<string>().HasMaxLength(3);
            conversion.Property(converted => converted.Amount).HasColumnName("BaseCurrencyAmount").HasPrecision(19, 4);
            conversion.Property(converted => converted.ExchangeRate).HasColumnName("ExchangeRate").HasPrecision(28, Domain.ExchangeRates.ExchangeRate.Decimals);
            conversion.Property(converted => converted.ExchangeRateDate).HasColumnName("ExchangeRateDate");
            conversion.Ignore(converted => converted.IsConverted);
        });
        transaction.Property(row => row.Payee).HasMaxLength(Transaction.MaxPayeeLength);
        transaction.Property(row => row.Note).HasMaxLength(Transaction.MaxNoteLength);
        transaction.Property<string?>(IdempotencyKey).HasMaxLength(CreateTransactionCommand.MaxIdempotencyKeyLength);
        transaction.Property<string?>(IdempotencyFingerprint).HasMaxLength(64);

        // PostgreSQL's own row version (xmin): it changes with every update, so a stale edit matches no row.
        transaction.Property(row => row.RowVersion).IsRowVersion();
        transaction.Ignore(row => row.IsDeleted);
        transaction.Ignore(row => row.SignedAmount);
        transaction.Ignore(row => row.SpentAmount);
        transaction.Ignore(row => row.IsConverted);
        transaction.Ignore(row => row.IsTransferLeg);

        // A category with transactions is archived, never deleted.
        transaction.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // The list, newest first; and the sum of an account's transactions, which is its balance (spec section 10).
        transaction.HasIndex(row => new { row.UserId, row.Date, row.Id }).IsDescending(false, true, true);
        transaction.HasIndex(row => new { row.UserId, row.AccountId, row.Date });

        // The transactions waiting for an exchange rate, which a fetch of new rates converts.
        transaction.HasIndex(row => row.UserId).HasFilter("\"BaseCurrencyAmount\" IS NULL").HasDatabaseName("IX_Transactions_UserId_NotConverted");

        // Finds the other leg, and a transfer has one leg of each type, never two (spec section 10).
        transaction.HasIndex(row => new { row.TransferId, row.Type }).IsUnique();

        // A retried request cannot record its transaction twice. Rows without a key never collide.
        transaction.HasIndex(nameof(Transaction.UserId), IdempotencyKey).IsUnique();
    }
}
