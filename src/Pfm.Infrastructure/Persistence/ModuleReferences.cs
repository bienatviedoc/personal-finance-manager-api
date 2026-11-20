using Microsoft.EntityFrameworkCore;
using Pfm.Domain.Accounts;
using Pfm.Domain.Transactions;

namespace Pfm.Infrastructure.Persistence;

/// <summary>
/// Foreign keys from one module's table to another's. They are set here because a module's own
/// configuration cannot name another module's types (see <c>ModuleBoundaryTests</c>).
/// </summary>
internal static class ModuleReferences
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        // An account with transactions is archived, never deleted.
        modelBuilder.Entity<Transaction>()
            .HasOne<Account>().WithMany().HasForeignKey(transaction => transaction.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
