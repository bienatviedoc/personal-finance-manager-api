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

    /// <summary>
    /// A line's category and kind are a category and that category's kind, as
    /// <see cref="CategoryKindIsItsCategorys"/> holds a transaction's: it covers the lines of a split after the
    /// first. Added by its migration alone.
    /// </summary>
    public const string LineCategoryKindIsItsCategorys = "FK_TransactionLines_Categories_CategoryId_CategoryKind";

    private const string TransactionId = "TransactionId";

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
        transaction.Ignore(row => row.IsSplit);

        // Lines are the transaction's own: read with it and saved with it. Only its row version guards them: an
        // edit of the lines always saves the transaction too (its UpdatedAt), so a stale one saves nothing.
        transaction.OwnsMany(row => row.Lines, line =>
        {
            line.ToTable("TransactionLines", table => table.HasCheckConstraint("CK_TransactionLines_Amount_Positive", "\"Amount\" > 0"));
            line.WithOwner().HasForeignKey(TransactionId);
            line.HasKey(row => row.Id);
            line.Property(row => row.Id).ValueGeneratedNever();
            line.Property(row => row.CategoryKind).HasConversion<string>().HasMaxLength(10);
            line.Property(row => row.Amount).HasPrecision(19, 4);
            line.Property(row => row.Note).HasMaxLength(Transaction.MaxNoteLength);

            // A category with transactions is archived, never deleted.
            line.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

            // What was spent in a category, for the list's filter, budgets and reports (spec section 10).
            line.HasIndex(nameof(TransactionLine.CategoryId), TransactionId);
            line.HasIndex(TransactionId, nameof(TransactionLine.Position));
        });
        transaction.Navigation(row => row.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        // A category with transactions is archived, never deleted.
        transaction.HasOne<Category>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // The list, newest first; and the sum of an account's transactions, which is its balance (spec section 10).
        transaction.HasIndex(row => new { row.UserId, row.Date, row.Id }).IsDescending(false, true, true);
        transaction.HasIndex(row => new { row.UserId, row.AccountId, row.Date });

        // Search: text anywhere in the payee or the note, found through their trigrams (spec section 10).
        transaction.HasIndex(row => new { row.Payee, row.Note })
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops", "gin_trgm_ops")
            .HasDatabaseName("IX_Transactions_Payee_Note_Trigrams");

        // The transactions waiting for an exchange rate, which a fetch of new rates converts.
        transaction.HasIndex(row => row.UserId).HasFilter("\"BaseCurrencyAmount\" IS NULL").HasDatabaseName("IX_Transactions_UserId_NotConverted");

        // A retried request cannot record its transaction twice. Rows without a key never collide.
        transaction.HasIndex(nameof(Transaction.UserId), IdempotencyKey).IsUnique();
    }
}
