using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class CategoryTests
{
    [Fact]
    public void A_subcategory_has_its_parents_kind()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");

        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", "#ef4444", parent: food);

        Assert.Equal(food.Id, groceries.ParentId);
        Assert.Equal(CategoryKind.Expense, groceries.Kind);
    }

    [Fact]
    public void A_subcategory_cannot_have_a_different_kind_from_its_parent()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            Category.Create("Refunds", CategoryKind.Income, "undo", "#22c55e", parent: food));

        Assert.Equal(nameof(Category.Kind), violation.Property);
    }

    [Fact]
    public void Categories_are_at_most_two_levels_deep()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", "#ef4444", parent: food);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            Category.Create("Vegetables", CategoryKind.Expense, "carrot", "#ef4444", parent: groceries));

        Assert.Equal(nameof(Category.ParentId), violation.Property);
    }

    [Fact]
    public void Changing_a_top_level_categorys_kind_changes_its_subcategories_too()
    {
        var gifts = Category.Create("Gifts", CategoryKind.Expense, "gift", "#a855f7");
        var birthdays = Category.Create("Birthdays", CategoryKind.Expense, "cake", "#a855f7", parent: gifts);

        gifts.ChangeKind(CategoryKind.Income, subcategories: [birthdays], hasTransactions: false);

        Assert.Equal(CategoryKind.Income, gifts.Kind);
        Assert.Equal(CategoryKind.Income, birthdays.Kind);
    }

    [Fact]
    public void A_subcategorys_kind_cannot_change_apart_from_its_parent()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", "#ef4444", parent: food);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            groceries.ChangeKind(CategoryKind.Income, subcategories: [], hasTransactions: false));

        Assert.Equal(nameof(Category.Kind), violation.Property);
        Assert.Equal(CategoryKind.Expense, groceries.Kind);
    }

    [Fact]
    public void A_categorys_kind_cannot_change_once_it_has_transactions()
    {
        var salary = Category.Create("Salary", CategoryKind.Income, "wallet", "#22c55e");

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            salary.ChangeKind(CategoryKind.Expense, subcategories: [], hasTransactions: true));

        Assert.Equal(nameof(Category.Kind), violation.Property);
        Assert.Equal(CategoryKind.Income, salary.Kind);
    }

    [Fact]
    public void Keeping_the_same_kind_is_allowed_whatever_the_transactions()
    {
        var salary = Category.Create("Salary", CategoryKind.Income, "wallet", "#22c55e");

        salary.ChangeKind(CategoryKind.Income, subcategories: [], hasTransactions: true);

        Assert.Equal(CategoryKind.Income, salary.Kind);
    }

    [Fact]
    public void Changing_the_kind_takes_only_the_categorys_own_subcategories()
    {
        var gifts = Category.Create("Gifts", CategoryKind.Expense, "gift", "#a855f7");
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", "#ef4444", parent: food);

        Assert.Throws<ArgumentException>(() => gifts.ChangeKind(CategoryKind.Income, subcategories: [groceries], hasTransactions: false));
    }

    [Fact]
    public void A_category_can_be_renamed_recoloured_and_given_another_icon()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");

        food.Rename("Food & drink");
        food.Recolour("#f97316");
        food.ChangeIcon("coffee");

        Assert.Equal(("Food & drink", "#f97316", "coffee"), (food.Name, food.Color, food.Icon));
    }

    [Fact]
    public void Archiving_a_category_archives_its_subcategories()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");
        var groceries = Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", "#ef4444", parent: food);

        food.Archive(subcategories: [groceries]);

        Assert.True(food.IsArchived);
        Assert.True(groceries.IsArchived);
    }

    [Fact]
    public void An_archived_category_takes_no_new_subcategories()
    {
        var food = Category.Create("Food", CategoryKind.Expense, "utensils", "#ef4444");
        food.Archive(subcategories: []);

        var violation = Assert.Throws<CategoryRuleViolation>(() =>
            Category.Create("Groceries", CategoryKind.Expense, "shopping-cart", "#ef4444", parent: food));

        Assert.Equal(nameof(Category.ParentId), violation.Property);
    }
}

public class DefaultCategoriesTests
{
    [Fact]
    public void The_default_set_has_the_same_shape_in_every_language()
    {
        static object[] Shape(IReadOnlyList<Category> set) =>
            [.. set.Select(category => (category.Kind, category.Icon, category.Color, IsSubcategory: category.ParentId is not null))];

        Assert.Equal(Shape(DefaultCategories.For(Language.English)), Shape(DefaultCategories.For(Language.Vietnamese)));
    }

    [Fact]
    public void The_default_set_has_income_and_expense_categories_with_subcategories()
    {
        var set = DefaultCategories.For(Language.Vietnamese);

        var food = Assert.Single(set, category => category.Name == "Ăn uống");
        Assert.Contains(set, category => category.Name == "Đi chợ" && category.ParentId == food.Id);
        Assert.Contains(set, category => category.Name == "Lương" && category.Kind == CategoryKind.Income);
    }

    [Fact]
    public void Each_call_gives_a_fresh_set()
    {
        Assert.NotEqual(DefaultCategories.For(Language.English)[0].Id, DefaultCategories.For(Language.English)[0].Id);
    }
}
