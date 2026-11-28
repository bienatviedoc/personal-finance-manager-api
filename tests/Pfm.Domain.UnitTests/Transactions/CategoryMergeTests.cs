using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

/// <summary>Merging one category into another (spec CAT-3): what the categories themselves become.</summary>
public class CategoryMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    [Fact]
    public void The_merged_category_is_archived()
    {
        var (coffee, eatingOut) = (Expense("Coffee"), Expense("Eating out"));

        coffee.MergeInto(eatingOut, subcategories: [], Later);

        Assert.True(coffee.IsArchived);
        Assert.Equal(Later, coffee.UpdatedAt);
        Assert.False(eatingOut.IsArchived);
    }

    [Fact]
    public void Only_categories_of_the_same_kind_can_be_merged()
    {
        var (bonus, food) = (Category.Create("Bonus", CategoryKind.Income, "award", Color.Parse("#10b981"), Now), Expense("Food"));

        Assert.Throws<CategoryRuleViolation>(() => bonus.MergeInto(food, [], Later));

        Assert.False(bonus.IsArchived);
    }

    [Fact]
    public void A_category_cannot_be_merged_into_itself()
    {
        var food = Expense("Food");

        Assert.Throws<CategoryRuleViolation>(() => food.MergeInto(food, [], Later));
    }

    [Fact]
    public void A_category_cannot_be_merged_into_an_archived_one()
    {
        var (coffee, drinks) = (Expense("Coffee"), Expense("Drinks"));
        drinks.Archive([], Now);

        Assert.Throws<CategoryRuleViolation>(() => coffee.MergeInto(drinks, [], Later));
    }

    [Fact]
    public void An_archived_category_can_be_merged_into_one_in_use()
    {
        var (coffee, drinks) = (Expense("Coffee"), Expense("Drinks"));
        coffee.Archive([], Now);

        coffee.MergeInto(drinks, [], Later);

        Assert.True(coffee.IsArchived);
    }

    [Fact]
    public void The_subcategories_of_a_merged_category_move_under_the_category_it_was_merged_into()
    {
        var (food, meals) = (Expense("Food"), Expense("Meals"));
        var groceries = Expense("Groceries", parent: food);

        food.MergeInto(meals, [groceries], Later);

        Assert.Equal(meals.Id, groceries.ParentId);
        Assert.False(groceries.IsArchived);
        Assert.Equal(Later, groceries.UpdatedAt);
    }

    [Fact]
    public void A_category_with_subcategories_cannot_be_merged_into_a_subcategory_which_would_make_three_levels()
    {
        var (food, transport) = (Expense("Food"), Expense("Transport"));
        var groceries = Expense("Groceries", parent: food);
        var taxi = Expense("Taxi", parent: transport);

        Assert.Throws<CategoryRuleViolation>(() => food.MergeInto(taxi, [groceries], Later));

        Assert.Equal(food.Id, groceries.ParentId);
        Assert.False(food.IsArchived);
    }

    [Fact]
    public void A_category_cannot_be_merged_into_its_own_subcategory()
    {
        var food = Expense("Food");
        var groceries = Expense("Groceries", parent: food);

        Assert.Throws<CategoryRuleViolation>(() => food.MergeInto(groceries, [groceries], Later));
    }

    [Fact]
    public void A_subcategory_can_be_merged_into_its_parent_or_into_any_other_category_of_its_kind()
    {
        var (food, transport) = (Expense("Food"), Expense("Transport"));
        var (groceries, coffee) = (Expense("Groceries", parent: food), Expense("Coffee", parent: food));

        groceries.MergeInto(food, [], Later);
        coffee.MergeInto(transport, [], Later);

        Assert.True(groceries.IsArchived);
        Assert.True(coffee.IsArchived);
    }

    [Fact]
    public void Only_the_categorys_own_subcategories_can_be_given()
    {
        var (food, meals, transport) = (Expense("Food"), Expense("Meals"), Expense("Transport"));
        var taxi = Expense("Taxi", parent: transport);

        Assert.Throws<ArgumentException>(() => food.MergeInto(meals, [taxi], Later));
    }

    [Fact]
    public void A_transaction_moves_each_of_its_lines_on_the_merged_category_and_keeps_the_rest()
    {
        var (coffee, eatingOut, groceries) = (Expense("Coffee"), Expense("Eating out"), Expense("Groceries"));
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var split = Transaction.RecordSplit(
            TransactionType.Expense, new AccountAmount(Guid.CreateVersion7(), Currency.VND, 300m), today,
            [new SplitLine(coffee, 100m), new SplitLine(groceries, 150m), new SplitLine(coffee, 50m, "Cake")], payee: null, note: null, new Moment(today, Now));

        split.MoveLines(coffee, eatingOut, Later);

        Assert.Equal([eatingOut.Id, groceries.Id, eatingOut.Id], split.Lines.Select(line => line.CategoryId));
        Assert.Equal([100m, 150m, 50m], split.Lines.Select(line => line.Amount));
        Assert.Equal("Cake", split.Lines[2].Note);
        Assert.Equal(eatingOut.Id, split.CategoryId);
        Assert.Equal(Later, split.UpdatedAt);
    }

    [Fact]
    public void A_deleted_transaction_moves_too_so_it_comes_back_on_the_category_merged_into()
    {
        var (coffee, eatingOut) = (Expense("Coffee"), Expense("Eating out"));
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var expense = Transaction.Record(TransactionType.Expense, new AccountAmount(Guid.CreateVersion7(), Currency.VND, 100m), today, coffee, null, null, new Moment(today, Now));
        expense.Delete(Now);

        expense.MoveLines(coffee, eatingOut, Later);

        Assert.Equal(eatingOut.Id, Assert.Single(expense.Lines).CategoryId);
    }

    [Fact]
    public void A_recurring_rule_on_the_merged_category_moves_to_the_category_merged_into()
    {
        var (coffee, eatingOut) = (Expense("Coffee"), Expense("Eating out"));
        var rule = RecurringRule.Create(
            TransactionType.Expense, Guid.CreateVersion7(), Currency.VND, 100m, coffee, null, null,
            RecurringSchedule.Of(RecurringFrequency.Monthly, null, new DateOnly(2030, 1, 1), null), Now);

        rule.MoveCategory(coffee, eatingOut, Later);

        Assert.Equal(eatingOut.Id, rule.CategoryId);
        Assert.Equal(Later, rule.UpdatedAt);
    }

    private static Category Expense(string name, Category? parent = null) =>
        Category.Create(name, CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now, parent);
}
