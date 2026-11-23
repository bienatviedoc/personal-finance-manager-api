using Pfm.Domain.Accounts;
using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class TransferTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Guid Savings = Guid.CreateVersion7();
    private static readonly Guid Dollars = Guid.CreateVersion7();

    [Fact]
    public void A_transfer_is_a_transfer_out_leg_on_the_source_account_and_a_transfer_in_leg_on_the_destination_sharing_its_id()
    {
        var transfer = Transfer.Record(
            new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), Today.AddDays(-1), "  for Tet ", Today, Monday);

        var (outLeg, inLeg) = (transfer.OutLeg, transfer.InLeg);
        Assert.Equal((TransactionType.TransferOut, Everyday, Currency.VND, 2_000_000m), (outLeg.Type, outLeg.AccountId, outLeg.Currency, outLeg.Amount));
        Assert.Equal((TransactionType.TransferIn, Savings, Currency.VND, 2_000_000m), (inLeg.Type, inLeg.AccountId, inLeg.Currency, inLeg.Amount));
        Assert.Equal((transfer.Id, transfer.Id), (outLeg.TransferId, inLeg.TransferId));
        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.NotEqual(outLeg.Id, inLeg.Id);
        Assert.All(new[] { outLeg, inLeg }, leg =>
        {
            Assert.True(leg.IsTransferLeg);
            Assert.Equal((Today.AddDays(-1), "for Tet", (Guid?)null, (string?)null), (leg.Date, leg.Note, leg.CategoryId, leg.Payee));
            Assert.Equal((Monday, Monday), (leg.CreatedAt, leg.UpdatedAt));
        });
    }

    [Fact]
    public void A_transfer_takes_its_amount_from_the_source_account_and_adds_it_to_the_destination()
    {
        var everyday = Account.Create("Everyday", AccountType.Current, null, Currency.VND, 5_000_000m, new DateOnly(2026, 1, 1), Monday);
        var savings = Account.Create("Savings", AccountType.Savings, null, Currency.VND, 0m, new DateOnly(2026, 1, 1), Monday);

        var transfer = Transfer.Record(new LegAmount(everyday.Id, Currency.VND, 2_000_000m), new LegAmount(savings.Id, Currency.VND, 2_000_000m), Today, null, Today, Monday);

        Assert.Equal(3_000_000m, everyday.BalanceWith(transfer.OutLeg.SignedAmount));
        Assert.Equal(2_000_000m, savings.BalanceWith(transfer.InLeg.SignedAmount));
    }

    [Fact]
    public void A_transfer_is_not_spending_so_it_moves_spent_in_no_category()
    {
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);
        var expense = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 125_000m, Today, groceries, null, null, Today, Monday);
        expense.ConvertInto(Currency.VND, 125_000m, 1m, Today);
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), Today, null, Today, Monday);
        transfer.OutLeg.ConvertInto(Currency.VND, 2_000_000m, 1m, Today);
        transfer.InLeg.ConvertInto(Currency.VND, 2_000_000m, 1m, Today);

        Assert.Equal((0m, 0m), (transfer.OutLeg.SpentAmount, transfer.InLeg.SpentAmount));
        Assert.Equal(125_000m, Spent.Of([expense, transfer.OutLeg, transfer.InLeg], [groceries.Id], Today.Year, Today.Month));
    }

    [Fact]
    public void A_leg_is_never_a_refund()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Savings, Currency.VND, 100m), Today, null, Today, Monday);

        Assert.False(transfer.OutLeg.IsRefund);
        Assert.False(transfer.InLeg.IsRefund);
        Assert.Throws<ArgumentException>(() => TransactionType.TransferOut.Signed(100m, isRefund: true));
        Assert.Throws<ArgumentException>(() => TransactionType.TransferIn.Signed(100m, isRefund: true));
    }

    [Fact]
    public void A_transfer_between_currencies_keeps_the_amount_entered_for_each_account()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_550_000m), new LegAmount(Dollars, Currency.USD, 99.5m), Today, null, Today, Monday);

        Assert.Equal((Currency.VND, 2_550_000m, -2_550_000m), (transfer.OutLeg.Currency, transfer.OutLeg.Amount, transfer.OutLeg.SignedAmount));
        Assert.Equal((Currency.USD, 99.5m, 99.5m), (transfer.InLeg.Currency, transfer.InLeg.Amount, transfer.InLeg.SignedAmount));
        Assert.Equal((2_550_000m, 99.5m), (transfer.Amount, transfer.ToAmount));
    }

    [Fact]
    public void Accounts_in_the_same_currency_move_the_same_amount()
    {
        var violation = Assert.Throws<TransferRuleViolation>(() =>
            Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 1_900_000m), Today, null, Today, Monday));

        Assert.Equal(nameof(Transfer.ToAmount), violation.Property);
    }

    [Fact]
    public void A_transfer_is_between_two_different_accounts()
    {
        var violation = Assert.Throws<TransferRuleViolation>(() =>
            Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Everyday, Currency.VND, 100m), Today, null, Today, Monday));

        Assert.Equal(nameof(Transfer.ToAccountId), violation.Property);
    }

    [Theory]
    [InlineData("0", "10", nameof(Transfer.Amount))]
    [InlineData("-1", "10", nameof(Transfer.Amount))]
    [InlineData("1000.5", "10", nameof(Transfer.Amount))]
    [InlineData("1000", "0", nameof(Transfer.ToAmount))]
    [InlineData("1000", "0.005", nameof(Transfer.ToAmount))]
    public void Each_amount_is_greater_than_zero_and_fits_the_currency_of_its_account(string amount, string toAmount, string property)
    {
        var violation = Assert.Throws<TransferRuleViolation>(() =>
            Transfer.Record(new LegAmount(Everyday, Currency.VND, Decimal(amount)), new LegAmount(Dollars, Currency.USD, Decimal(toAmount)), Today, null, Today, Monday));

        Assert.Equal(property, violation.Property);
    }

    [Fact]
    public void A_transfer_cannot_be_dated_in_the_future()
    {
        var violation = Assert.Throws<TransferRuleViolation>(() =>
            Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Savings, Currency.VND, 100m), Today.AddDays(1), null, Today, Monday));

        Assert.Equal(nameof(Transfer.Date), violation.Property);
    }

    [Fact]
    public void The_two_legs_read_back_in_any_order_pair_into_their_transfer()
    {
        var recorded = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_550_000m), new LegAmount(Dollars, Currency.USD, 99.5m), Today, "holiday", Today, Monday);

        var transfer = Transfer.Of([recorded.InLeg, recorded.OutLeg]);

        Assert.Equal(recorded.Id, transfer.Id);
        Assert.Same(recorded.OutLeg, transfer.OutLeg);
        Assert.Same(recorded.InLeg, transfer.InLeg);
        Assert.Equal((Everyday, Dollars, Today, "holiday"), (transfer.FromAccountId, transfer.ToAccountId, transfer.Date, transfer.Note));
    }

    [Fact]
    public void Transactions_that_are_not_the_two_legs_of_one_transfer_do_not_pair()
    {
        var one = Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Savings, Currency.VND, 100m), Today, null, Today, Monday);
        var other = Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Savings, Currency.VND, 100m), Today, null, Today, Monday);
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);
        var expense = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 100m, Today, groceries, null, null, Today, Monday);

        Assert.Throws<ArgumentException>(() => Transfer.Of([one.OutLeg]));
        Assert.Throws<ArgumentException>(() => Transfer.Of([one.OutLeg, other.InLeg]));
        Assert.Throws<ArgumentException>(() => Transfer.Of([one.OutLeg, one.OutLeg]));
        Assert.Throws<ArgumentException>(() => Transfer.Of([one.OutLeg, expense]));
        Assert.Throws<ArgumentException>(() => Transfer.Of([one.OutLeg, one.InLeg, other.InLeg]));
    }

    [Fact]
    public void Editing_a_transfer_changes_both_legs_together()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), Today, "for Tet", Today, Monday);
        var (outId, inId) = (transfer.OutLeg.Id, transfer.InLeg.Id);

        transfer.Edit(new LegAmount(Savings, Currency.VND, 2_550_000m), new LegAmount(Dollars, Currency.USD, 99.5m), Today.AddDays(-2), null, Today, Tuesday);

        var (outLeg, inLeg) = (transfer.OutLeg, transfer.InLeg);
        Assert.Equal((outId, TransactionType.TransferOut, Savings, Currency.VND, 2_550_000m), (outLeg.Id, outLeg.Type, outLeg.AccountId, outLeg.Currency, outLeg.Amount));
        Assert.Equal((inId, TransactionType.TransferIn, Dollars, Currency.USD, 99.5m), (inLeg.Id, inLeg.Type, inLeg.AccountId, inLeg.Currency, inLeg.Amount));
        Assert.All(new[] { outLeg, inLeg }, leg => Assert.Equal((Today.AddDays(-2), (string?)null, Monday, Tuesday), (leg.Date, leg.Note, leg.CreatedAt, leg.UpdatedAt)));
    }

    [Fact]
    public void An_edit_that_breaks_a_rule_changes_neither_leg()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Dollars, Currency.USD, 80m), Today, null, Today, Monday);

        var violation = Assert.Throws<TransferRuleViolation>(() =>
            transfer.Edit(new LegAmount(Everyday, Currency.VND, 3_000_000m), new LegAmount(Dollars, Currency.USD, 0m), Today, null, Today, Tuesday));

        Assert.Equal(nameof(Transfer.ToAmount), violation.Property);
        Assert.Equal((2_000_000m, 80m, Monday), (transfer.OutLeg.Amount, transfer.InLeg.Amount, transfer.OutLeg.UpdatedAt));
    }

    [Fact]
    public void A_leg_whose_amount_date_or_account_changed_is_converted_into_the_base_currency_again()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_550_000m), new LegAmount(Dollars, Currency.USD, 100m), Today, null, Today, Monday);
        transfer.OutLeg.ConvertInto(Currency.VND, 2_550_000m, 1m, Today);
        transfer.InLeg.ConvertInto(Currency.VND, 2_550_000m, 25_500m, Today);

        transfer.Edit(new LegAmount(Everyday, Currency.VND, 2_550_000m), new LegAmount(Dollars, Currency.USD, 99.5m), Today, "less the fee", Today, Tuesday);

        Assert.True(transfer.OutLeg.IsConverted);
        Assert.False(transfer.InLeg.IsConverted);
    }

    [Fact]
    public void Deleting_a_transfer_deletes_both_legs_and_gives_both_accounts_their_money_back()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), Today, null, Today, Monday);

        transfer.Delete(Tuesday);

        Assert.True(transfer.IsDeleted);
        Assert.All(new[] { transfer.OutLeg, transfer.InLeg }, leg =>
        {
            Assert.Equal((Tuesday, Tuesday, 0m), (leg.DeletedAt, leg.UpdatedAt, leg.SignedAmount));
        });
    }

    [Fact]
    public void A_deleted_transfer_stays_deleted_as_it_was_and_cannot_be_edited()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Savings, Currency.VND, 100m), Today, null, Today, Monday);
        transfer.Delete(Monday);

        transfer.Delete(Tuesday);

        Assert.Equal(Monday, transfer.OutLeg.DeletedAt);
        Assert.Throws<InvalidOperationException>(() =>
            transfer.Edit(new LegAmount(Everyday, Currency.VND, 200m), new LegAmount(Savings, Currency.VND, 200m), Today, null, Today, Tuesday));
    }

    [Fact]
    public void A_leg_is_never_edited_or_deleted_on_its_own()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 100m), new LegAmount(Savings, Currency.VND, 100m), Today, null, Today, Monday);
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);

        Assert.Throws<InvalidOperationException>(() => transfer.OutLeg.Delete(Tuesday));
        Assert.Throws<InvalidOperationException>(() =>
            transfer.InLeg.Edit(TransactionType.Expense, Savings, Currency.VND, 100m, Today, groceries, null, null, Today, Tuesday));
        Assert.False(transfer.IsDeleted);
    }

    [Theory]
    [InlineData(TransactionType.TransferOut)]
    [InlineData(TransactionType.TransferIn)]
    public void Income_and_expenses_are_the_only_transactions_recorded_on_their_own(TransactionType type)
    {
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);

        var violation = Assert.Throws<TransactionRuleViolation>(() =>
            Transaction.Record(type, Everyday, Currency.VND, 100m, Today, groceries, null, null, Today, Monday));

        Assert.Equal(nameof(Transaction.Type), violation.Property);
    }

    private static decimal Decimal(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
