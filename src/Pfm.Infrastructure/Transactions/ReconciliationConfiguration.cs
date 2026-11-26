using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Transactions;

internal sealed class ReconciliationConfiguration : IEntityTypeConfiguration<Reconciliation>
{
    public void Configure(EntityTypeBuilder<Reconciliation> reconciliation)
    {
        reconciliation.ToTable("Reconciliations");
        reconciliation.HasKey(row => row.Id);
        reconciliation.Property(row => row.Id).ValueGeneratedNever();
        reconciliation.Property(row => row.StatementBalance).HasPrecision(19, 4);

        // PostgreSQL's own row version (xmin): a tick saves the reconciliation, so a completion made on what it read cannot miss it.
        reconciliation.Property(row => row.RowVersion).IsRowVersion();
        reconciliation.Ignore(row => row.IsCompleted);
        reconciliation.Ignore(row => row.Status);

        // An account has one reconciliation in progress at a time: of two started at the same moment, the second is refused.
        reconciliation.HasIndex(row => row.AccountId).IsUnique().HasFilter("\"CompletedAt\" IS NULL").HasDatabaseName("IX_Reconciliations_AccountId_InProgress");

        // The account's reconciliations, the last started first.
        reconciliation.HasIndex(row => new { row.AccountId, row.Id });

        // A transaction is ticked in at most one reconciliation; one is cancelled only once nothing is ticked in it.
        reconciliation.HasMany<Transaction>().WithOne().HasForeignKey(transaction => transaction.ReconciliationId).OnDelete(DeleteBehavior.Restrict);
    }
}
