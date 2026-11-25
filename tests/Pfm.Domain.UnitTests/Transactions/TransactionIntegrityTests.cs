using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class TransactionIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 30);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Guid Savings = Guid.CreateVersion7();
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Now);
    private static readonly Category Household = Category.Create("Household", CategoryKind.Expense, "home", Color.Parse("#ea580c"), Now);
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Now);

    [Fact]
    public void Transactions_that_add_up_show_no_drift()
    {
        var salary = Transaction.Record(TransactionType.Income, Everyday, Currency.VND, 15_000_000m, Today, Salary, null, null, Today, Now);
        var shopping = Transaction.RecordSplit(
            TransactionType.Expense, Everyday, Currency.VND, 500_000m, Today, [new SplitLine(Groceries, 320_000m), new SplitLine(Household, 180_000m)], null, null, Today, Now);
        var refund = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 20_000m, Today, Groceries, null, null, Today, Now, isRefund: true);
        var deleted = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 99_000m, Today, Groceries, null, null, Today, Now);
        deleted.Delete(Now);
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), Today, null, Today, Now);
        var cancelled = Transfer.Record(new LegAmount(Everyday, Currency.VND, 1_000m), new LegAmount(Savings, Currency.VND, 1_000m), Today, null, Today, Now);
        cancelled.Delete(Now);

        var drift = TransactionIntegrity.Check(
            [salary, shopping, refund, deleted, transfer.OutLeg, transfer.InLeg, cancelled.OutLeg, cancelled.InLeg],
            new Dictionary<Guid, decimal> { [Everyday] = 12_520_000m, [Savings] = 2_000_000m });

        Assert.Empty(drift);
    }

    [Fact]
    public void A_balance_that_is_not_what_its_accounts_transactions_come_to_is_drift_naming_the_account_and_both_amounts()
    {
        var salary = Transaction.Record(TransactionType.Income, Everyday, Currency.VND, 15_000_000m, Today, Salary, null, null, Today, Now);
        var lunch = Transaction.Record(TransactionType.Expense, Savings, Currency.VND, 45_000m, Today, Groceries, null, null, Today, Now);

        var drift = TransactionIntegrity.Check([salary, lunch], new Dictionary<Guid, decimal> { [Everyday] = 14_000_000m });

        Assert.Equal(
            new HashSet<Drift> { new Drift.Balance(Everyday, Shown: 14_000_000m, FromTransactions: 15_000_000m), new Drift.Balance(Savings, Shown: 0m, FromTransactions: -45_000m) },
            drift.ToHashSet());
    }

    [Fact]
    public void A_transfer_with_one_leg_is_drift_naming_the_transfer_and_the_leg_it_has()
    {
        var transfer = Transfer.Record(new LegAmount(Everyday, Currency.VND, 2_000_000m), new LegAmount(Savings, Currency.VND, 2_000_000m), Today, null, Today, Now);

        var drift = TransactionIntegrity.Check([transfer.OutLeg], new Dictionary<Guid, decimal> { [Everyday] = -2_000_000m });

        var found = Assert.IsType<Drift.Transfer>(Assert.Single(drift));
        Assert.Equal((transfer.Id, TransferDrift.NotTwoLegs), (found.TransferId, found.Problem));
        Assert.Equal([transfer.OutLeg.Id], found.LegIds);
    }
}
