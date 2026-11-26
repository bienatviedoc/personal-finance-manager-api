using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

/// <summary>What a bulk action does to each transaction it acts on (spec TXN-7).</summary>
public class BulkEditTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Guid Savings = Guid.CreateVersion7();
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Monday);
    private static readonly Category Groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ea580c"), Monday);
    private static readonly Category Household = Category.Create("Household", CategoryKind.Expense, "home", Color.Parse("#ea580c"), Monday);

    [Fact]
    public void Recategorising_puts_the_whole_amount_on_the_new_category_and_keeps_the_lines_note()
    {
        var transaction = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 1000m, Today, Groceries, null, null, Today, Monday);

        transaction.Recategorise(Household, Tuesday);

        Assert.Equal(Household.Id, transaction.CategoryId);
        var line = Assert.Single(transaction.Lines);
        Assert.Equal((Household.Id, 1000m), (line.CategoryId, line.Amount));
        Assert.Equal(Tuesday, transaction.UpdatedAt);
    }

    [Fact]
    public void A_category_of_the_wrong_kind_or_a_split_transaction_is_refused_a_new_category()
    {
        var expense = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 1000m, Today, Groceries, null, null, Today, Monday);
        var split = Transaction.RecordSplit(
            TransactionType.Expense, Everyday, Currency.VND, 1000m, Today, [new SplitLine(Groceries, 400m), new SplitLine(Household, 600m)], null, null, Today, Monday);

        Assert.Equal(nameof(Transaction.CategoryId), Assert.Throws<TransactionRuleViolation>(() => expense.Recategorise(Salary, Tuesday)).Property);
        Assert.Equal(nameof(Transaction.Lines), Assert.Throws<TransactionRuleViolation>(() => split.Recategorise(Household, Tuesday)).Property);
        Assert.Equal(Groceries.Id, expense.CategoryId);
    }

    [Fact]
    public void A_move_to_an_account_in_the_same_currency_keeps_everything_else_and_one_in_another_is_refused()
    {
        var transaction = Transaction.RecordSplit(
            TransactionType.Expense, Everyday, Currency.VND, 1000m, Today, [new SplitLine(Groceries, 400m), new SplitLine(Household, 600m)], "Co.op", null, Today, Monday);

        transaction.MoveTo(Savings, Currency.VND, Tuesday);

        Assert.Equal((Savings, 1000m, "Co.op", 2), (transaction.AccountId, transaction.Amount, transaction.Payee, transaction.Lines.Count));
        Assert.Equal(nameof(Transaction.AccountId), Assert.Throws<TransactionRuleViolation>(() => transaction.MoveTo(Everyday, Currency.USD, Tuesday)).Property);
        Assert.Equal(Savings, transaction.AccountId);
    }

    [Fact]
    public void A_tag_is_added_once_and_removed_and_a_twenty_first_is_refused()
    {
        var transaction = Transaction.Record(TransactionType.Expense, Everyday, Currency.VND, 1000m, Today, Groceries, null, null, Today, Monday);
        var trip = Tag.Create("japan-trip", Monday);

        transaction.AddTag(trip, Tuesday);
        transaction.AddTag(trip, Tuesday);
        Assert.Equal(["japan-trip"], transaction.Tags.Select(tag => tag.Name));

        transaction.RemoveTag(trip.Id, Tuesday);
        Assert.Empty(transaction.Tags);

        foreach (var number in Enumerable.Range(1, Transaction.MaxTags))
        {
            transaction.AddTag(Tag.Create($"tag-{number}", Monday), Tuesday);
        }

        Assert.Equal(nameof(Transaction.Tags), Assert.Throws<TransactionRuleViolation>(() => transaction.AddTag(trip, Tuesday)).Property);
    }
}
