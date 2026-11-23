using Microsoft.EntityFrameworkCore;
using Pfm.Domain.Accounts;
using Pfm.Domain.Budgets;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Identity;
using Pfm.Infrastructure.Transactions;

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

    /// <summary>
    /// A budget's user and currency are a user and that user's base currency. So once a user has a budget the
    /// database refuses a change of their base currency (spec section 3, rule 12), also one saved a moment
    /// after their first budget, and refuses a budget set in a base currency its user has just left.
    /// </summary>
    public const string BudgetCurrencyIsItsUsersBaseCurrency = "FK_Budgets_Users_UserId_Currency";

    /// <summary>
    /// A budget's category is an expense category. So the database refuses a budget on a category that has
    /// just become an income one, and a change of kind on a category a budget has just been set on.
    /// </summary>
    public const string BudgetCategoryIsAnExpenseCategory = "FK_Budgets_Categories_CategoryId_CategoryKind";

    public static void Configure(ModelBuilder modelBuilder)
    {
        // A category with budgets is archived, never deleted.
        modelBuilder.Entity<Budget>()
            .HasOne<Category>().WithMany().HasForeignKey(budget => budget.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // An account with transactions is archived, never deleted.
        modelBuilder.Entity<Transaction>()
            .HasOne<Account>().WithMany().HasForeignKey(transaction => transaction.AccountId).OnDelete(DeleteBehavior.Restrict);

        // An account a recurring rule posts to is archived, never deleted.
        modelBuilder.Entity<RecurringRule>()
            .HasOne<Account>().WithMany().HasForeignKey(rule => rule.AccountId).OnDelete(DeleteBehavior.Restrict);

        // What the foreign keys named above point at (a category's id and kind are indexed by its own configuration). The keys themselves are in the migration that adds them:
        // the model cannot hold a foreign key to columns that change, or one checked at commit.
        modelBuilder.Entity<Account>().HasIndex(account => new { account.Id, account.Currency }).IsUnique();
        modelBuilder.Entity<User>().HasIndex(user => new { user.Id, user.BaseCurrency }).IsUnique();
    }

    /// <summary>Whether the constraint a save broke is one of those that another request's save can break.</summary>
    public static bool IsBrokenByAConcurrentSave(string? constraintName) =>
        constraintName is TransactionCurrencyIsItsAccounts or TransactionBaseCurrencyIsItsUsers or TransactionConfiguration.CategoryKindIsItsCategorys
            or TransactionConfiguration.LineCategoryKindIsItsCategorys or BudgetCurrencyIsItsUsersBaseCurrency or BudgetCategoryIsAnExpenseCategory;
}
