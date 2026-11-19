using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class CategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    [Fact]
    public void A_subcategory_has_its_parents_kind()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);

        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ef4444"), Now, parent: food);

        Assert.Equal(food.Id, groceries.ParentId);
        Assert.Equal(CategoryKind.Expense, groceries.Kind);
    }

    [Fact]
    public void A_subcategory_cannot_have_a_different_kind_from_its_parent()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            Category.Create("Refunds", CategoryKind.Income, "undo", Color.Parse("#22c55e"), Now, parent: food));

        Assert.Equal(nameof(Category.Kind), violation.Property);
    }

    [Fact]
    public void Categories_are_at_most_two_levels_deep()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ef4444"), Now, parent: food);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            Category.Create("Vegetables", CategoryKind.Expense, "carrot", Color.Parse("#ef4444"), Now, parent: groceries));

        Assert.Equal(nameof(Category.ParentId), violation.Property);
    }

    [Fact]
    public void Changing_a_top_level_categorys_kind_changes_its_subcategories_too()
    {
        var gifts = Category.Create("Gifts", CategoryKind.Expense, "gift", Color.Parse("#a855f7"), Now);
        var birthdays = Category.Create("Birthdays", CategoryKind.Expense, "cake", Color.Parse("#a855f7"), Now, parent: gifts);

        gifts.ChangeKind(CategoryKind.Income, subcategories: [birthdays], hasTransactions: false, Now);

        Assert.Equal(CategoryKind.Income, gifts.Kind);
        Assert.Equal(CategoryKind.Income, birthdays.Kind);
    }

    [Fact]
    public void A_subcategorys_kind_cannot_change_apart_from_its_parent()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ef4444"), Now, parent: food);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            groceries.ChangeKind(CategoryKind.Income, subcategories: [], hasTransactions: false, Now));

        Assert.Equal(nameof(Category.Kind), violation.Property);
        Assert.Equal(CategoryKind.Expense, groceries.Kind);
    }

    [Fact]
    public void A_categorys_kind_cannot_change_once_it_has_transactions()
    {
        var salary = Category.Create("Salary", CategoryKind.Income, "wallet", Color.Parse("#22c55e"), Now);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            salary.ChangeKind(CategoryKind.Expense, subcategories: [], hasTransactions: true, Now));

        Assert.Equal(nameof(Category.Kind), violation.Property);
        Assert.Equal(CategoryKind.Income, salary.Kind);
    }

    [Fact]
    public void Keeping_the_same_kind_is_allowed_whatever_the_transactions()
    {
        var salary = Category.Create("Salary", CategoryKind.Income, "wallet", Color.Parse("#22c55e"), Now);

        salary.ChangeKind(CategoryKind.Income, subcategories: [], hasTransactions: true, Now);

        Assert.Equal(CategoryKind.Income, salary.Kind);
    }

    [Fact]
    public void Changing_the_kind_takes_only_the_categorys_own_subcategories()
    {
        var gifts = Category.Create("Gifts", CategoryKind.Expense, "gift", Color.Parse("#a855f7"), Now);
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ef4444"), Now, parent: food);

        Assert.Throws<ArgumentException>(() => gifts.ChangeKind(CategoryKind.Income, subcategories: [groceries], hasTransactions: false, Now));
    }

    [Fact]
    public void A_category_can_be_renamed_recoloured_and_given_another_icon()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);

        food.Edit("Food & drink", "coffee", Color.Parse("#f97316"), Later);

        Assert.Equal(("Food & drink", "#f97316", "coffee", Later), (food.Name, food.Color.Hex, food.Icon, food.UpdatedAt));
    }

    [Theory]
    [InlineData(" ", "utensils", "#ef4444")]
    [InlineData("Food", "Utensils", "#ef4444")]
    public void A_category_needs_a_name_and_an_icon_key(string name, string icon, string color)
    {
        Assert.Throws<ArgumentException>(() => Category.Create(name, CategoryKind.Expense, icon, Color.Parse(color), Now));
    }

    [Fact]
    public void Archiving_an_archived_category_changes_nothing()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
        food.Archive(subcategories: [], Now);

        food.Archive(subcategories: [], Later);

        Assert.Equal(Now, food.UpdatedAt);
    }

    [Fact]
    public void Archiving_a_category_archives_its_subcategories()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ef4444"), Now, parent: food);

        food.Archive(subcategories: [groceries], Now);

        Assert.True(food.IsArchived);
        Assert.True(groceries.IsArchived);
    }

    [Fact]
    public void An_archived_category_takes_no_new_subcategories()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
        food.Archive(subcategories: [], Now);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", Color.Parse("#ef4444"), Now, parent: food));

        Assert.Equal(nameof(Category.ParentId), violation.Property);
    }
}

public class DefaultCategoriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_default_set_has_the_same_shape_in_every_language()
    {
        static object[] Shape(IReadOnlyList<Category> set) =>
            [.. set.Select(category => (category.Kind, category.Icon, category.Color, IsSubcategory: category.ParentId is not null))];

        Assert.Equal(Shape(DefaultCategories.For(Language.English, Now)), Shape(DefaultCategories.For(Language.Vietnamese, Now)));
    }

    [Fact]
    public void The_default_set_has_income_and_expense_categories_with_subcategories()
    {
        var set = DefaultCategories.For(Language.Vietnamese, Now);

        var food = Assert.Single(set, category => category.Name == "Ăn uống");
        Assert.Contains(set, category => category.Name == "Đi chợ" && category.ParentId == food.Id);
        Assert.Contains(set, category => category.Name == "Lương" && category.Kind == CategoryKind.Income);
    }

    [Fact]
    public void Each_call_gives_a_fresh_set()
    {
        Assert.NotEqual(DefaultCategories.For(Language.English, Now)[0].Id, DefaultCategories.For(Language.English, Now)[0].Id);
    }
}
