using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class MarkAsTransferTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Guid Visa = Guid.CreateVersion7();
    private static readonly Guid Dollars = Guid.CreateVersion7();
    private static readonly Category Fees = Category.Create("Fees", CategoryKind.Expense, "receipt", Color.Parse("#64748b"), Monday);
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);
    private static readonly Category OtherIncome = Category.Create("Other income", CategoryKind.Income, "ellipsis", Color.Parse("#22c55e"), Monday);

    [Fact]
    public void An_expense_and_an_income_on_another_account_become_the_transfer_out_and_transfer_in_legs_of_one_transfer()
    {
        var expense = Expense(Everyday, 1_500_000m, payee: "VISA PAYMENT", note: "card");
        var income = Income(Visa, 1_500_000m, note: "payment received");

        var transfer = Transfer.Mark(expense, income, Tuesday);

        Assert.Same(expense, transfer.OutLeg);
        Assert.Same(income, transfer.InLeg);
        Assert.Equal((TransactionType.TransferOut, Everyday, 1_500_000m), (expense.Type, expense.AccountId, expense.Amount));
        Assert.Equal((TransactionType.TransferIn, Visa, 1_500_000m), (income.Type, income.AccountId, income.Amount));
        Assert.Equal((transfer.Id, transfer.Id), (expense.TransferId, income.TransferId));
        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.All(new[] { expense, income }, leg =>
        {
            Assert.True(leg.IsTransferLeg);
            Assert.Empty(leg.Lines);
            Assert.Equal(((Guid?)null, (string?)null, Today, Tuesday), (leg.CategoryId, leg.Payee, leg.Date, leg.UpdatedAt));
        });
        Assert.Equal(("card", "payment received"), (expense.Note, income.Note));
    }

    [Fact]
    public void Once_marked_the_two_are_neither_spending_nor_income_and_still_move_both_balances()
    {
        var expense = Expense(Everyday, 1_500_000m);
        expense.ConvertInto(Currency.VND, 1_500_000m, 1m, Today);
        var income = Income(Visa, 1_500_000m);
        Assert.Equal(1_500_000m, Spent.Of([expense], [Fees.Id], Today.Year, Today.Month));

        var transfer = Transfer.Mark(expense, income, Tuesday);

        Assert.Equal(0m, Spent.Of([transfer.OutLeg, transfer.InLeg], [Fees.Id], Today.Year, Today.Month));
        Assert.Equal((-1_500_000m, 1_500_000m), (transfer.OutLeg.SignedAmount, transfer.InLeg.SignedAmount));
        Assert.Empty(TransactionIntegrity.Check([transfer.OutLeg, transfer.InLeg], new Dictionary<Guid, decimal> { [Everyday] = -1_500_000m, [Visa] = 1_500_000m }));
    }

    [Fact]
    public void A_split_expense_is_marked_whole()
    {
        var expense = Transaction.RecordSplit(
            TransactionType.Expense, Everyday, Currency.VND, 300m, Today, [new SplitLine(Fees, 100m), new SplitLine(Groceries, 200m)], null, null, Today, Monday);

        var transfer = Transfer.Mark(expense, Income(Visa, 300m), Tuesday);

        Assert.Empty(transfer.OutLeg.Lines);
        Assert.False(transfer.OutLeg.IsSplit);
    }

    [Fact]
    public void Between_accounts_in_different_currencies_each_keeps_its_own_amount_and_conversion()
    {
        var expense = Expense(Everyday, 2_550_000m);
        expense.ConvertInto(Currency.VND, 2_550_000m, 1m, Today);
        var income = Transaction.Record(TransactionType.Income, Dollars, Currency.USD, 99.5m, Today, OtherIncome, null, null, Today, Monday);
        income.ConvertInto(Currency.VND, 2_540_000m, 25_527.64m, Today);

        var transfer = Transfer.Mark(expense, income, Tuesday);

        Assert.Equal((2_550_000m, 99.5m), (transfer.Amount, transfer.ToAmount));
        Assert.Equal((2_550_000m, 2_540_000m), (transfer.OutLeg.Conversion!.Amount, transfer.InLeg.Conversion!.Amount));
    }

    [Fact]
    public void Both_on_one_account_is_refused()
    {
        var violation = Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(Expense(Everyday, 100m), Income(Everyday, 100m), Tuesday));

        Assert.Equal(Transfer.Income, violation.Property);
    }

    [Fact]
    public void Accounts_in_one_currency_must_have_moved_the_same_amount()
    {
        var violation = Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(Expense(Everyday, 100m), Income(Visa, 90m), Tuesday));

        Assert.Equal(Transfer.Income, violation.Property);
    }

    [Fact]
    public void The_two_must_be_on_the_same_date()
    {
        var income = Transaction.Record(TransactionType.Income, Visa, Currency.VND, 100m, Today.AddDays(-1), OtherIncome, null, null, Today, Monday);

        var violation = Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(Expense(Everyday, 100m), income, Tuesday));

        Assert.Equal(Transfer.Income, violation.Property);
    }

    [Fact]
    public void Only_an_expense_that_is_not_a_refund_becomes_the_transfer_out_leg_and_only_income_the_transfer_in_leg()
    {
        var refund = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 100m, Today, Fees, null, null, Today, Monday, isRefund: true);

        Assert.Equal(Transfer.Expense, Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(refund, Income(Visa, 100m), Tuesday)).Property);
        Assert.Equal(Transfer.Expense, Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(Income(Everyday, 100m), Income(Visa, 100m), Tuesday)).Property);
        Assert.Equal(Transfer.Income, Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(Expense(Everyday, 100m), Expense(Visa, 100m), Tuesday)).Property);
    }

    [Fact]
    public void A_leg_of_a_transfer_already_is_refused_and_neither_transaction_changes()
    {
        var first = Transfer.Mark(Expense(Everyday, 100m), Income(Visa, 100m), Tuesday);
        var income = Income(Dollars, 100m);

        var violation = Assert.Throws<TransferRuleViolation>(() => Transfer.Mark(first.OutLeg, income, Tuesday));

        Assert.Equal(Transfer.Expense, violation.Property);
        Assert.Equal((first.Id, TransactionType.Income, (Guid?)null), (first.OutLeg.TransferId, income.Type, income.TransferId));
    }

    [Fact]
    public void A_deleted_transaction_cannot_be_marked()
    {
        var expense = Expense(Everyday, 100m);
        expense.Delete(Monday);

        Assert.Throws<InvalidOperationException>(() => Transfer.Mark(expense, Income(Visa, 100m), Tuesday));
    }

    [Fact]
    public void An_occurrence_of_a_recurring_rule_is_no_longer_one_once_marked()
    {
        var expense = Expense(Everyday, 100m);
        expense.PostedBy(Guid.CreateVersion7());

        var transfer = Transfer.Mark(expense, Income(Visa, 100m), Tuesday);

        Assert.Null(transfer.OutLeg.RecurringRuleId);
    }

    [Fact]
    public void A_transfer_unmarked_is_an_expense_and_an_income_again_on_the_categories_chosen()
    {
        var transfer = Transfer.Mark(Expense(Everyday, 1_500_000m, note: "card"), Income(Visa, 1_500_000m), Monday);

        var (expense, income) = transfer.Unmark(Fees, OtherIncome, Tuesday);

        Assert.Equal((TransactionType.Expense, Everyday, 1_500_000m, Today, "card"), (expense.Type, expense.AccountId, expense.Amount, expense.Date, expense.Note));
        Assert.Equal((TransactionType.Income, Visa, 1_500_000m), (income.Type, income.AccountId, income.Amount));
        Assert.Equal(((Guid?)Fees.Id, (Guid?)OtherIncome.Id), (expense.CategoryId, income.CategoryId));
        Assert.All(new[] { expense, income }, transaction =>
        {
            Assert.False(transaction.IsTransferLeg);
            Assert.Null(transaction.TransferId);
            Assert.Single(transaction.Lines);
            Assert.Equal(transaction.Amount, transaction.Lines[0].Amount);
            Assert.Equal(Tuesday, transaction.UpdatedAt);
        });
    }

    [Fact]
    public void Any_transfer_can_be_unmarked_into_an_expense_and_an_income()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Visa, Currency.VND, 100m), Today, null, Today, Monday);

        var (expense, income) = transfer.Unmark(Fees, OtherIncome, Tuesday);

        Assert.Equal((TransactionType.Expense, TransactionType.Income), (expense.Type, income.Type));
    }

    [Fact]
    public void Unmarking_takes_an_expense_category_for_the_expense_and_an_income_category_for_the_income_that_are_not_archived()
    {
        var archived = Category.Create("Old", CategoryKind.Expense, "receipt", Color.Parse("#64748b"), Monday);
        archived.Archive([], Monday);

        Assert.Equal(Transfer.ExpenseCategory, Assert.Throws<TransferRuleViolation>(() => Marked().Unmark(OtherIncome, OtherIncome, Tuesday)).Property);
        Assert.Equal(Transfer.IncomeCategory, Assert.Throws<TransferRuleViolation>(() => Marked().Unmark(Fees, Fees, Tuesday)).Property);
        Assert.Equal(Transfer.ExpenseCategory, Assert.Throws<TransferRuleViolation>(() => Marked().Unmark(archived, OtherIncome, Tuesday)).Property);
    }

    [Fact]
    public void A_refused_unmark_leaves_both_legs_as_they_were()
    {
        var transfer = Marked();

        Assert.Throws<TransferRuleViolation>(() => transfer.Unmark(Fees, Fees, Tuesday));

        Assert.Equal((TransactionType.TransferOut, TransactionType.TransferIn), (transfer.OutLeg.Type, transfer.InLeg.Type));
        Assert.Equal((transfer.Id, transfer.Id), (transfer.OutLeg.TransferId, transfer.InLeg.TransferId));
    }

    [Fact]
    public void A_deleted_transfer_cannot_be_unmarked()
    {
        var transfer = Marked();
        transfer.Delete(Monday);

        Assert.Throws<InvalidOperationException>(() => transfer.Unmark(Fees, OtherIncome, Tuesday));
    }

    private static Transfer Marked() => Transfer.Mark(Expense(Everyday, 100m), Income(Visa, 100m), Monday);

    private static Transaction Expense(Guid account, decimal amount, string? payee = null, string? note = null) =>
        Transaction.Record(TransactionType.Expense, account, Currency.VND, amount, Today, Fees, payee, note, Today, Monday);

    private static Transaction Income(Guid account, decimal amount, string? note = null) =>
        Transaction.Record(TransactionType.Income, account, Currency.VND, amount, Today, OtherIncome, null, note, Today, Monday);
}
