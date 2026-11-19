namespace Pfm.Domain.Transactions;

/// <summary>
/// What a transaction was for, at most two levels deep (Food > Groceries). Its kind is either income or
/// expense, shared with its parent and fixed once it has transactions.
/// </summary>
public sealed class Category : IUserOwned
{
    private Category(string name, CategoryKind kind, string icon, string color, Guid? parentId)
    {
        Name = name;
        Kind = kind;
        Icon = icon;
        Color = color;
        ParentId = parentId;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid UserId { get; private set; }

    /// <summary>The top-level category this one sits under; null for a top-level category.</summary>
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; }

    public CategoryKind Kind { get; private set; }

    /// <summary>The key of the icon shown with the category.</summary>
    public string Icon { get; private set; }

    /// <summary>A hex colour such as <c>#ef4444</c>.</summary>
    public string Color { get; private set; }

    /// <summary>Hidden from pickers; kept on the transactions that already use it.</summary>
    public bool IsArchived { get; private set; }

    /// <summary>A top-level category, or a subcategory of <paramref name="parent"/>.</summary>
    public static Category Create(string name, CategoryKind kind, string icon, string color, Category? parent = null)
    {
        if (parent is not null)
        {
            if (parent.ParentId is not null)
            {
                throw new CategoryRuleViolation(nameof(ParentId), "Categories can be at most two levels deep.");
            }

            if (parent.IsArchived)
            {
                throw new CategoryRuleViolation(nameof(ParentId), "An archived category takes no new subcategories.");
            }

            if (kind != parent.Kind)
            {
                throw new CategoryRuleViolation(nameof(Kind), "A subcategory has its parent's kind.");
            }
        }

        return new Category(name, kind, icon, color, parent?.Id);
    }

    public void Rename(string name) => Name = name;

    public void Recolour(string color) => Color = color;

    public void ChangeIcon(string icon) => Icon = icon;

    /// <summary>
    /// Changes the kind of a top-level category and, with it, of its subcategories, which always share it.
    /// Refused for a subcategory, and once the category or any of its subcategories has transactions.
    /// </summary>
    /// <param name="subcategories">All of this category's subcategories.</param>
    /// <param name="hasTransactions">Whether this category or any of its subcategories has transactions.</param>
    public void ChangeKind(CategoryKind kind, IReadOnlyCollection<Category> subcategories, bool hasTransactions)
    {
        EnsureOwn(subcategories);
        if (kind == Kind)
        {
            return;
        }

        if (ParentId is not null)
        {
            throw new CategoryRuleViolation(nameof(Kind), "A subcategory has its parent's kind.");
        }

        if (hasTransactions)
        {
            throw new CategoryRuleViolation(nameof(Kind), "A category's kind cannot change once it has transactions.");
        }

        Kind = kind;
        foreach (var subcategory in subcategories)
        {
            subcategory.Kind = kind;
        }
    }

    /// <summary>Hides the category, and its subcategories with it, from pickers. Transactions keep it.</summary>
    /// <param name="subcategories">All of this category's subcategories.</param>
    public void Archive(IReadOnlyCollection<Category> subcategories)
    {
        EnsureOwn(subcategories);
        IsArchived = true;
        foreach (var subcategory in subcategories)
        {
            subcategory.IsArchived = true;
        }
    }

    private void EnsureOwn(IReadOnlyCollection<Category> subcategories)
    {
        if (subcategories.Any(subcategory => subcategory.ParentId != Id))
        {
            throw new ArgumentException("Only this category's own subcategories can be given.", nameof(subcategories));
        }
    }
}
