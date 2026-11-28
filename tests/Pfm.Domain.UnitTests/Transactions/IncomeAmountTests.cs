using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class IncomeAmountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 30);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Now);
    private static readonly Category Bonus = Category.Create("Bonus", CategoryKind.Income, "gift", Color.Parse("#16a34a"), Now);
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Now);

    [Fact]
    public void Income_counts_its_converted_amount_in_the_categories_of_its_lines()
    {
        var paid = Transaction.Record(TransactionType.Income, new AccountAmount(Everyday, Currency.USD, 1_000m), new DateOnly(2026, 11, 2), Salary, null, null, new Moment(Today, Now));
        paid.ConvertInto(Currency.VND, 25_000_000m, 25_000m, new DateOnly(2026, 11, 2));

        Assert.Equal(25_000_000m, paid.IncomeAmountIn([Salary.Id]));
        Assert.Equal(0m, paid.IncomeAmountIn([Bonus.Id]));
    }

    [Fact]
    public void A_split_income_shares_its_converted_amount_among_its_lines()
    {
        var paid = Transaction.RecordSplit(
            TransactionType.Income, new AccountAmount(Everyday, Currency.VND, 30_000_000m), new DateOnly(2026, 11, 2),
            [new SplitLine(Salary, 20_000_000m), new SplitLine(Bonus, 10_000_000m)], null, null, new Moment(Today, Now));
        paid.ConvertInto(Currency.VND, 30_000_000m, 1m, new DateOnly(2026, 11, 2));

        Assert.Equal((20_000_000m, 10_000_000m), (paid.IncomeAmountIn([Salary.Id]), paid.IncomeAmountIn([Bonus.Id])));
    }

    [Fact]
    public void An_expense_a_deleted_income_and_one_not_converted_are_no_income()
    {
        var spent = Transaction.Record(TransactionType.Expense, new AccountAmount(Everyday, Currency.VND, 100m), new DateOnly(2026, 11, 2), Groceries, null, null, new Moment(Today, Now));
        spent.ConvertInto(Currency.VND, 100m, 1m, new DateOnly(2026, 11, 2));
        var deleted = Transaction.Record(TransactionType.Income, new AccountAmount(Everyday, Currency.VND, 100m), new DateOnly(2026, 11, 2), Salary, null, null, new Moment(Today, Now));
        deleted.ConvertInto(Currency.VND, 100m, 1m, new DateOnly(2026, 11, 2));
        deleted.Delete(Now);
        var waiting = Transaction.Record(TransactionType.Income, new AccountAmount(Everyday, Currency.USD, 100m), new DateOnly(2026, 11, 2), Salary, null, null, new Moment(Today, Now));
        waiting.FlagNotConverted(Currency.VND);

        Assert.Equal([0m, 0m, 0m], new[] { spent, deleted, waiting }.Select(transaction => transaction.IncomeAmountIn([Salary.Id, Groceries.Id])));
    }
}
