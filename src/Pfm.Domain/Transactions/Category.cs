namespace Pfm.Domain.Transactions;

/// <summary>
/// What a transaction was for, at most two levels deep (Food > Groceries). Its kind is either income or
/// expense, shared with its parent and fixed once it has transactions.
/// </summary>
public sealed class Category : IUserOwned
{
    public const int MaxNameLength = 100;

    /// <summary>For persistence, which sets every property itself.</summary>
    private Category()
    {
        Name = Icon = Color = "";
    }

    private Category(string name, CategoryKind kind, string icon, string color, Guid? parentId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
        UpdatedAt = now;
        Name = name;
        Kind = kind;
        Icon = icon;
        Color = color;
        ParentId = parentId;
    }

    public Guid Id { get; private set; }

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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxNameLength;

    /// <summary>An icon key: lower-case letters, digits and hyphens, as the SPA names its icons.</summary>
    public static bool IsValidIcon(string? icon) =>
        icon is { Length: > 0 and <= 50 } && icon.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    /// <summary>A colour is written <c>#rrggbb</c>, in either case.</summary>
    public static bool IsValidColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit);

    /// <summary>A top-level category, or a subcategory of <paramref name="parent"/>.</summary>
    public static Category Create(string name, CategoryKind kind, string icon, string color, DateTimeOffset now, Category? parent = null)
    {
        EnsureValid(name, icon, color);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A category is income or expense.");
        }

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

        return new Category(name, kind, icon, color, parent?.Id, now);
    }

    /// <summary>Renames, recolours and re-icons the category.</summary>
    public void Edit(string name, string icon, string color, DateTimeOffset now)
    {
        EnsureValid(name, icon, color);
        Name = name;
        Icon = icon;
        Color = color;
        UpdatedAt = now;
    }

    /// <summary>
    /// Changes the kind of a top-level category and, with it, of its subcategories, which always share it.
    /// Refused for a subcategory, and once the category or any of its subcategories has transactions.
    /// </summary>
    /// <param name="subcategories">All of this category's subcategories.</param>
    /// <param name="hasTransactions">Whether this category or any of its subcategories has transactions.</param>
    public void ChangeKind(CategoryKind kind, IReadOnlyCollection<Category> subcategories, bool hasTransactions, DateTimeOffset now)
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

        foreach (var category in subcategories.Prepend(this))
        {
            category.Kind = kind;
            category.UpdatedAt = now;
        }
    }

    /// <summary>Hides the category, and its subcategories with it, from pickers. Transactions keep it.</summary>
    /// <param name="subcategories">All of this category's subcategories.</param>
    public void Archive(IReadOnlyCollection<Category> subcategories, DateTimeOffset now)
    {
        EnsureOwn(subcategories);
        foreach (var category in subcategories.Prepend(this).Where(category => !category.IsArchived))
        {
            category.IsArchived = true;
            category.UpdatedAt = now;
        }
    }

    private static void EnsureValid(string name, string icon, string color)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"A category's name is required and at most {MaxNameLength} characters.", nameof(name));
        }

        if (!IsValidIcon(icon))
        {
            throw new ArgumentException("A category's icon is a key of lower-case letters, digits and hyphens.", nameof(icon));
        }

        if (!IsValidColor(color))
        {
            throw new ArgumentException("A category's colour is written #rrggbb.", nameof(color));
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
