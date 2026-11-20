using Microsoft.EntityFrameworkCore;
using Pfm.Domain.Accounts;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Identity;

namespace Pfm.Infrastructure.Persistence;

/// <summary>
/// Foreign keys from one module's table to another's. They are set here because a module's own
/// configuration cannot name another module's types (see <c>ModuleBoundaryTests</c>).
/// </summary>
internal static class ModuleReferences
{
    /// <summary>
    /// A transaction's account and currency are an account and that account's currency. So the database
    /// refuses a transaction recorded in a currency its account has just left, and a change of currency on an
    /// account a transaction has just been recorded on, whichever of the two requests saves second.
    /// </summary>
    public const string TransactionCurrencyIsItsAccounts = "FK_Transactions_Accounts_AccountId_Currency";

    /// <summary>
    /// A transaction's user and base currency are a user and that user's base currency. Checked when the save
    /// commits, because a change of base currency saves the user and their transactions together: it is refused
    /// when a transaction was recorded meanwhile in the old one, and so is a transaction converted into a base
    /// currency its user has just left.
    /// </summary>
    public const string TransactionBaseCurrencyIsItsUsers = "FK_Transactions_Users_UserId_BaseCurrency";

    public static void Configure(ModelBuilder modelBuilder)
    {
        // An account with transactions is archived, never deleted.
        modelBuilder.Entity<Transaction>()
            .HasOne<Account>().WithMany().HasForeignKey(transaction => transaction.AccountId).OnDelete(DeleteBehavior.Restrict);

        // What the two foreign keys above point at. The keys themselves are in the migration that adds them:
        // the model cannot hold a foreign key to columns that change, or one checked at commit.
        modelBuilder.Entity<Account>().HasIndex(account => new { account.Id, account.Currency }).IsUnique();
        modelBuilder.Entity<User>().HasIndex(user => new { user.Id, user.BaseCurrency }).IsUnique();
    }

    /// <summary>Whether the constraint a save broke is one of the two that another request's save can break.</summary>
    public static bool IsBrokenByAConcurrentSave(string? constraintName) =>
        constraintName is TransactionCurrencyIsItsAccounts or TransactionBaseCurrencyIsItsUsers;
}
