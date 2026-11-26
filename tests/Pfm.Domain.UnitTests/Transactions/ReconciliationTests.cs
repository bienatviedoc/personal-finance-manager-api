using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class ReconciliationTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateOnly StatementDate = new(2026, 10, 31);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Guid Savings = Guid.CreateVersion7();
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Monday);

    [Fact]
    public void A_reconciliation_starts_on_an_account_with_a_statement_date_and_closing_balance()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 4_500_000m, Today, Monday);

        Assert.Equal((Everyday, StatementDate, 4_500_000m), (reconciliation.AccountId, reconciliation.StatementDate, reconciliation.StatementBalance));
        Assert.False(reconciliation.IsCompleted);
        Assert.Null(reconciliation.CompletedAt);
        Assert.NotEqual(Guid.Empty, reconciliation.Id);
    }

    [Fact]
    public void A_statement_balance_can_be_below_zero_as_a_credit_card_is()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, -1_500_000m, Today, Monday);

        Assert.Equal(-1_500_000m, reconciliation.StatementBalance);
    }

    [Fact]
    public void A_statement_date_cannot_be_in_the_future()
    {
        var violation = Assert.Throws<ReconciliationRuleViolation>(() => Reconciliation.Start(Everyday, Currency.VND, Today.AddDays(1), 0m, Today, Monday));

        Assert.Equal(nameof(Reconciliation.StatementDate), violation.Property);
    }

    [Fact]
    public void A_statement_balance_has_no_more_decimals_than_the_currency()
    {
        var violation = Assert.Throws<ReconciliationRuleViolation>(() => Reconciliation.Start(Everyday, Currency.USD, StatementDate, 10.005m, Today, Monday));

        Assert.Equal(nameof(Reconciliation.StatementBalance), violation.Property);
    }

    [Fact]
    public void Ticking_transactions_moves_the_difference_between_the_statement_balance_and_the_ticked_total()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 5_500_000m, Today, Monday);
        var salary = Income(5_000_000m);
        var groceries = Expense(500_000m);

        Assert.Equal(4_500_000m, reconciliation.DifferenceWith(1_000_000m, []));

        reconciliation.Tick(salary, Tuesday);
        Assert.Equal(-500_000m, reconciliation.DifferenceWith(1_000_000m, [salary]));

        reconciliation.Tick(groceries, Tuesday);
        Assert.Equal(0m, reconciliation.DifferenceWith(1_000_000m, [salary, groceries]));
        Assert.Equal(4_500_000m, reconciliation.TickedTotal([salary, groceries]));
        Assert.Equal((reconciliation.Id, reconciliation.Id), (salary.ReconciliationId, groceries.ReconciliationId));
        Assert.Equal(Tuesday, reconciliation.UpdatedAt);
    }

    [Fact]
    public void A_ticked_total_counts_only_what_is_ticked_in_this_reconciliation_and_not_deleted()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 0m, Today, Monday);
        var ticked = Expense(100_000m);
        var deleted = Expense(200_000m);
        var notTicked = Expense(400_000m);
        reconciliation.Tick(ticked, Tuesday);
        reconciliation.Tick(deleted, Tuesday);
        deleted.Delete(Tuesday);

        Assert.Equal(-100_000m, reconciliation.TickedTotal([ticked, deleted, notTicked]));
    }

    [Fact]
    public void Unticking_takes_a_transaction_out_of_the_ticked_total()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 0m, Today, Monday);
        var groceries = Expense(500_000m);
        reconciliation.Tick(groceries, Tuesday);

        reconciliation.Untick(groceries, Tuesday);

        Assert.Null(groceries.ReconciliationId);
        Assert.Equal(0m, reconciliation.TickedTotal([groceries]));
    }

    [Fact]
    public void Only_a_transaction_on_the_account_dated_on_or_before_the_statement_date_can_be_ticked()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 0m, Today, Monday);
        var elsewhere = Transaction.Record(TransactionType.Expense, Savings, Currency.VND, 1_000m, StatementDate, Groceries, null, null, Today, Monday);
        var later = Expense(1_000m, StatementDate.AddDays(1));

        Assert.Equal("TransactionId", Assert.Throws<ReconciliationRuleViolation>(() => reconciliation.Tick(elsewhere, Tuesday)).Property);
        Assert.Equal("TransactionId", Assert.Throws<ReconciliationRuleViolation>(() => reconciliation.Tick(later, Tuesday)).Property);
        Assert.Null(elsewhere.ReconciliationId);
        Assert.Null(later.ReconciliationId);
    }

    [Fact]
    public void A_transaction_already_reconciled_cannot_be_ticked_in_another_reconciliation()
    {
        var first = Reconciliation.Start(Everyday, Currency.VND, StatementDate, -1_000m, Today, Monday);
        var groceries = Expense(1_000m);
        first.Tick(groceries, Tuesday);
        first.Complete(0m, [groceries], Tuesday);
        var second = Reconciliation.Start(Everyday, Currency.VND, Today, -1_000m, Today, Tuesday);

        var violation = Assert.Throws<ReconciliationRuleViolation>(() => second.Tick(groceries, Tuesday));

        Assert.Equal("TransactionId", violation.Property);
        Assert.Equal(first.Id, groceries.ReconciliationId);
    }

    [Fact]
    public void It_completes_only_when_the_difference_is_exactly_zero()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 4_500_000m, Today, Monday);
        var salary = Income(5_000_000m);
        reconciliation.Tick(salary, Tuesday);

        var violation = Assert.Throws<ReconciliationRuleViolation>(() => reconciliation.Complete(0m, [salary], Tuesday));

        Assert.Equal("Difference", violation.Property);
        Assert.False(reconciliation.IsCompleted);
        Assert.False(salary.IsReconciled);
    }

    [Fact]
    public void Completing_locks_the_ticked_transactions()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 4_500_000m, Today, Monday);
        var salary = Income(5_000_000m);
        var groceries = Expense(500_000m);
        var notTicked = Expense(1_000m);
        reconciliation.Tick(salary, Tuesday);
        reconciliation.Tick(groceries, Tuesday);

        reconciliation.Complete(0m, [salary, groceries, notTicked], Tuesday);

        Assert.True(reconciliation.IsCompleted);
        Assert.Equal(Tuesday, reconciliation.CompletedAt);
        Assert.True(salary.IsReconciled);
        Assert.True(groceries.IsReconciled);
        Assert.False(notTicked.IsReconciled);
    }

    [Fact]
    public void A_completed_reconciliation_takes_no_ticks_until_it_is_reopened()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 0m, Today, Monday);
        reconciliation.Complete(0m, [], Tuesday);
        var groceries = Expense(1_000m);

        Assert.Throws<ReconciliationRuleViolation>(() => reconciliation.Tick(groceries, Tuesday));
        Assert.Throws<ReconciliationRuleViolation>(() => reconciliation.Untick(groceries, Tuesday));
    }

    [Fact]
    public void Reopening_unlocks_its_transactions_and_keeps_them_ticked()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, -500_000m, Today, Monday);
        var groceries = Expense(500_000m);
        reconciliation.Tick(groceries, Tuesday);
        reconciliation.Complete(0m, [groceries], Tuesday);

        reconciliation.Reopen([groceries], Tuesday);

        Assert.False(reconciliation.IsCompleted);
        Assert.Null(reconciliation.CompletedAt);
        Assert.False(groceries.IsReconciled);
        Assert.Equal(reconciliation.Id, groceries.ReconciliationId);
        groceries.Edit(TransactionType.Expense, Everyday, Currency.VND, 600_000m, StatementDate, Groceries, null, null, Today, Tuesday);
        Assert.Equal(600_000m, groceries.Amount);
    }

    [Theory]
    [InlineData("Amount")]
    [InlineData("Date")]
    [InlineData("AccountId")]
    [InlineData("Type")]
    [InlineData("IsRefund")]
    public void A_reconciled_transaction_keeps_what_its_balance_depends_on(string field)
    {
        var groceries = ReconciledExpense(500_000m);
        var (type, category, account, amount, date, isRefund) = (TransactionType.Expense, Groceries, Everyday, 500_000m, StatementDate, false);
        switch (field)
        {
            case "Amount": amount = 600_000m; break;
            case "Date": date = StatementDate.AddDays(-1); break;
            case "AccountId": account = Savings; break;
            case "Type": (type, category) = (TransactionType.Income, Salary); break;
            case "IsRefund": isRefund = true; break;
        }

        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            groceries.Edit(type, account, Currency.VND, amount, date, category, null, null, Today, Tuesday, isRefund));

        Assert.Equal(field, violation.Property);
        Assert.Equal((500_000m, StatementDate, Everyday, TransactionType.Expense), (groceries.Amount, groceries.Date, groceries.AccountId, groceries.Type));
    }

    [Fact]
    public void A_reconciled_transaction_can_still_change_its_payee_note_and_category()
    {
        var groceries = ReconciledExpense(500_000m);
        var dining = Category.Create("Dining", CategoryKind.Expense, "utensils", Color.Parse("#dc2626"), Monday);

        groceries.Edit(TransactionType.Expense, Everyday, Currency.VND, 500_000m, StatementDate, dining, "Bep Me In", "lunch", Today, Tuesday);

        Assert.Equal((dining.Id, "Bep Me In", "lunch"), (groceries.CategoryId, groceries.Payee, groceries.Note));
        Assert.True(groceries.IsReconciled);
    }

    [Fact]
    public void A_reconciled_transaction_cannot_be_deleted()
    {
        var groceries = ReconciledExpense(500_000m);

        Assert.Throws<TransactionRuleViolation>(() => groceries.Delete(Tuesday));
        Assert.False(groceries.IsDeleted);
    }

    [Fact]
    public void A_ticked_transaction_moved_to_another_account_is_no_longer_ticked()
    {
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 0m, Today, Monday);
        var groceries = Expense(500_000m);
        reconciliation.Tick(groceries, Tuesday);

        groceries.Edit(TransactionType.Expense, Savings, Currency.VND, 500_000m, StatementDate, Groceries, null, null, Today, Tuesday);

        Assert.Null(groceries.ReconciliationId);
    }

    [Fact]
    public void A_transfer_is_locked_as_soon_as_either_leg_is_reconciled()
    {
        var transfer = Transfer.Record(
            new LegAmount(Everyday, Currency.VND, 1_000_000m), new LegAmount(Savings, Currency.VND, 1_000_000m), StatementDate, null, Today, Monday);
        var reconciliation = Reconciliation.Start(Savings, Currency.VND, StatementDate, 1_000_000m, Today, Monday);
        reconciliation.Tick(transfer.InLeg, Tuesday);
        reconciliation.Complete(0m, [transfer.InLeg], Tuesday);

        Assert.True(transfer.IsReconciled);
        Assert.Equal("Amount", Assert.Throws<TransferRuleViolation>(() =>
            transfer.Edit(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), StatementDate, null, Today, Tuesday)).Property);
        Assert.Equal("Date", Assert.Throws<TransferRuleViolation>(() =>
            transfer.Edit(new LegAmount(Everyday, Currency.VND, 1_000_000m), new LegAmount(Savings, Currency.VND, 1_000_000m), Today, null, Today, Tuesday)).Property);
        Assert.Equal("FromAccountId", Assert.Throws<TransferRuleViolation>(() =>
            transfer.Edit(new LegAmount(Guid.CreateVersion7(), Currency.VND, 1_000_000m), new LegAmount(Savings, Currency.VND, 1_000_000m), StatementDate, null, Today, Tuesday)).Property);
        Assert.Throws<TransactionRuleViolation>(() => transfer.Delete(Tuesday));
        Assert.Equal((1_000_000m, false), (transfer.Amount, transfer.IsDeleted));

        transfer.Edit(new LegAmount(Everyday, Currency.VND, 1_000_000m), new LegAmount(Savings, Currency.VND, 1_000_000m), StatementDate, "rent pot", Today, Tuesday);
        Assert.Equal("rent pot", transfer.Note);
    }

    [Fact]
    public void A_reconciled_transaction_cannot_be_marked_as_a_leg_of_a_transfer()
    {
        var reconciledExpense = ReconciledExpense(500_000m);
        var income = Transaction.Record(TransactionType.Income, Savings, Currency.VND, 500_000m, StatementDate, Salary, null, null, Today, Monday);

        var violation = Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(reconciledExpense, income, Tuesday));

        Assert.Equal(Transfer.ExpenseId, violation.Property);
        Assert.Equal((TransactionType.Expense, TransactionType.Income), (reconciledExpense.Type, income.Type));

        var expense = Transaction.Record(TransactionType.Expense, Savings, Currency.VND, 500_000m, StatementDate, Groceries, null, null, Today, Monday);
        var reconciledIncome = Income(500_000m);
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, 500_000m, Today, Monday);
        reconciliation.Tick(reconciledIncome, Monday);
        reconciliation.Complete(0m, [reconciledIncome], Monday);

        Assert.Equal(Transfer.IncomeId, Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(expense, reconciledIncome, Tuesday)).Property);
        Assert.Null(expense.TransferId);
    }

    [Fact]
    public void A_reconciled_transfer_cannot_be_unmarked()
    {
        var transfer = Transfer.Record(
            new LegAmount(Everyday, Currency.VND, 1_000_000m), new LegAmount(Savings, Currency.VND, 1_000_000m), StatementDate, null, Today, Monday);
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, -1_000_000m, Today, Monday);
        reconciliation.Tick(transfer.OutLeg, Tuesday);
        reconciliation.Complete(0m, [transfer.OutLeg], Tuesday);

        Assert.Throws<TransferRuleViolation>(() => transfer.Unmark(Groceries, Salary, Tuesday));
        Assert.Equal((TransactionType.TransferOut, TransactionType.TransferIn), (transfer.OutLeg.Type, transfer.InLeg.Type));
    }

    private static Transaction Income(decimal amount) =>
        Transaction.Record(TransactionType.Income, Everyday, Currency.VND, amount, StatementDate, Salary, null, null, Today, Monday);

    private static Transaction Expense(decimal amount, DateOnly? date = null) =>
        Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, amount, date ?? StatementDate, Groceries, null, null, Today, Monday);

    private static Transaction ReconciledExpense(decimal amount)
    {
        var expense = Expense(amount);
        var reconciliation = Reconciliation.Start(Everyday, Currency.VND, StatementDate, -amount, Today, Monday);
        reconciliation.Tick(expense, Monday);
        reconciliation.Complete(0m, [expense], Monday);
        return expense;
    }
}
