using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class SpentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 30);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Category Clothing = Category.Create("Clothing", CategoryKind.Expense, "shirt", Color.Parse("#ea580c"), Now);
    private static readonly Category Shoes = Category.Create("Shoes", CategoryKind.Expense, "footprints", Color.Parse("#ea580c"), Now, parent: Clothing);
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Now);
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Now);

    [Fact]
    public void Spent_is_the_expenses_of_a_category_and_its_subcategories_in_the_month()
    {
        Transaction[] transactions =
        [
            Expense(800_000m, Clothing, new DateOnly(2026, 11, 2)),
            Expense(1_200_000m, Shoes, new DateOnly(2026, 11, 30)),
            Expense(999m, Clothing, new DateOnly(2026, 10, 31)),
            Expense(999m, Clothing, new DateOnly(2025, 11, 2)),
            Expense(125_000m, Groceries, new DateOnly(2026, 11, 2)),
            Converted(Transaction.Record(TransactionType.Income, Everyday, Currency.VND, 15_000_000m, new DateOnly(2026, 11, 2), Salary, null, null, Today, Now)),
        ];

        Assert.Equal(2_000_000m, Spent.Of(transactions, [Clothing.Id, Shoes.Id], 2026, 11));
        Assert.Equal(1_200_000m, Spent.Of(transactions, [Shoes.Id], 2026, 11));
        Assert.Equal(0m, Spent.Of(transactions, [Clothing.Id, Shoes.Id], 2026, 9));
    }

    [Fact]
    public void A_refund_reduces_spent_in_its_category_in_the_month_of_the_refund()
    {
        Transaction[] transactions =
        [
            Expense(800_000m, Clothing, new DateOnly(2026, 10, 28)),
            Expense(500_000m, Clothing, new DateOnly(2026, 11, 5)),
            Refund(300_000m, Clothing, new DateOnly(2026, 11, 9)),
            Refund(50_000m, Groceries, new DateOnly(2026, 11, 9)),
        ];

        Assert.Equal(200_000m, Spent.Of(transactions, [Clothing.Id], 2026, 11));
        Assert.Equal(800_000m, Spent.Of(transactions, [Clothing.Id], 2026, 10));
    }

    [Fact]
    public void Spent_goes_below_zero_when_a_refund_lands_in_a_month_with_less_spending_and_is_never_clamped()
    {
        Transaction[] transactions =
        [
            Expense(800_000m, Clothing, new DateOnly(2026, 10, 28)),
            Refund(800_000m, Clothing, new DateOnly(2026, 11, 3)),
            Expense(100_000m, Shoes, new DateOnly(2026, 11, 4)),
        ];

        Assert.Equal(-700_000m, Spent.Of(transactions, [Clothing.Id, Shoes.Id], 2026, 11));
        Assert.Equal(-800_000m, Spent.Of(transactions, [Clothing.Id], 2026, 11));
    }

    [Fact]
    public void Spent_is_in_the_base_currency_and_a_refund_takes_away_its_own_converted_amount()
    {
        var bought = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 40m, new DateOnly(2026, 11, 2), Clothing, null, null, Today, Now);
        bought.ConvertInto(Currency.VND, 1_000_000m, 25_000m, new DateOnly(2026, 11, 2));
        var refunded = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 40m, new DateOnly(2026, 11, 10), Clothing, null, null, Today, Now, isRefund: true);
        refunded.ConvertInto(Currency.VND, 1_040_000m, 26_000m, new DateOnly(2026, 11, 10));

        Assert.Equal((1_000_000m, -1_040_000m), (bought.SpentAmount, refunded.SpentAmount));
        Assert.Equal(-40_000m, Spent.Of([bought, refunded], [Clothing.Id], 2026, 11));
    }

    [Fact]
    public void A_deleted_transaction_and_one_not_converted_are_left_out_of_spent()
    {
        var deleted = Refund(300_000m, Clothing, new DateOnly(2026, 11, 9));
        deleted.Delete(Now);
        var notConverted = Transaction.Record(TransactionType.Expense, Everyday, Currency.USD, 40m, new DateOnly(2026, 11, 2), Clothing, null, null, Today, Now, isRefund: true);
        notConverted.FlagNotConverted(Currency.VND);
        var neverSaved = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 40m, new DateOnly(2026, 11, 2), Clothing, null, null, Today, Now);

        Assert.Equal(500_000m, Spent.Of([Expense(500_000m, Clothing, new DateOnly(2026, 11, 5)), deleted, notConverted, neverSaved], [Clothing.Id], 2026, 11));
    }

    private static Transaction Expense(decimal amount, Category category, DateOnly date) =>
        Converted(Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, amount, date, category, null, null, Today, Now));

    private static Transaction Refund(decimal amount, Category category, DateOnly date) =>
        Converted(Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, amount, date, category, null, null, Today, Now, isRefund: true));

    /// <summary>In the base currency already, so converted at a rate of one.</summary>
    private static Transaction Converted(Transaction transaction)
    {
        transaction.ConvertInto(Currency.VND, transaction.Amount, 1m, transaction.Date);
        return transaction;
    }
}
