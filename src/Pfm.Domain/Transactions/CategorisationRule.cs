namespace Pfm.Domain.Transactions;

/// <summary>How a categorisation rule's match text is looked for in a payee or description.</summary>
public enum CategorisationMatch
{
    /// <summary>Anywhere in it.</summary>
    Contains = 1,

    /// <summary>At its start.</summary>
    StartsWith = 2,

    /// <summary>The whole of it.</summary>
    Exactly = 3,
}

/// <summary>A change that would break a rule of categorisation rules. Names the rule property at fault.</summary>
public sealed class CategorisationRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}

/// <summary>
/// One of the user's rules for categorising transactions (spec IMP-4): a payee, or an imported row's description,
/// that contains, starts with or is <see cref="MatchText"/> takes <see cref="CategoryId"/>. Text is compared as
/// <see cref="ComparableText"/> has it: without case, accents or extra spaces. A rule categorises only transactions of
/// its category's kind, and none while its category is archived. Of several rules that match, the one of highest
/// <see cref="Priority"/> wins (<see cref="Categoriser"/>).
/// </summary>
public sealed class CategorisationRule : IUserOwned
{
    public const int MaxMatchTextLength = 100;

    /// <summary>The highest priority a rule can have.</summary>
    public const int MaxPriority = 1_000_000;

    /// <summary>For persistence, which sets every property itself.</summary>
    private CategorisationRule()
    {
        MatchText = "";
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>What is looked for, trimmed.</summary>
    public string MatchText { get; private set; }

    public CategorisationMatch Match { get; private set; }

    /// <summary>The category a matching transaction takes.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>From 0; of several rules that match, the highest wins, and of equal ones the one set up first.</summary>
    public int Priority { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static bool IsValidMatchText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= MaxMatchTextLength;

    public static bool IsValidPriority(int priority) => priority is >= 0 and <= MaxPriority;

    /// <summary>A new rule, on a category that is not archived.</summary>
    public static CategorisationRule Create(string matchText, CategorisationMatch match, Category category, int priority, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        EnsureValid(matchText, match, priority);
        GuardChoosable(category);
        return new CategorisationRule
        {
            Id = Guid.CreateVersion7(now),
            MatchText = matchText.Trim(),
            Match = match,
            CategoryId = category.Id,
            Priority = priority,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Changes every part of the rule. It may keep a category archived since, but not move to one.</summary>
    public void Edit(string matchText, CategorisationMatch match, Category category, int priority, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        EnsureValid(matchText, match, priority);
        if (category.Id != CategoryId)
        {
            GuardChoosable(category);
        }

        MatchText = matchText.Trim();
        Match = match;
        CategoryId = category.Id;
        Priority = priority;
        UpdatedAt = now;
    }

    /// <summary>Sets the priority alone, as reordering the rules does.</summary>
    public void Prioritise(int priority, DateTimeOffset now)
    {
        if (!IsValidPriority(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, $"A priority is from 0 to {MaxPriority}.");
        }

        if (priority != Priority)
        {
            Priority = priority;
            UpdatedAt = now;
        }
    }

    /// <summary>
    /// Moves the rule from <paramref name="from"/> onto <paramref name="into"/>, as merging the one category into the
    /// other does (spec CAT-3), so it goes on categorising on a category that is not archived.
    /// </summary>
    public void MoveCategory(Category from, Category into, DateTimeOffset now)
    {
        Category.GuardMerge(from, into);
        if (CategoryId != from.Id)
        {
            return;
        }

        CategoryId = into.Id;
        UpdatedAt = now;
    }

    /// <summary>Whether the payee or description has the match text the rule's way.</summary>
    public bool Matches(string? text)
    {
        var comparable = ComparableText.Of(text);
        if (comparable.Length == 0)
        {
            return false;
        }

        var looked = ComparableText.Of(MatchText);
        return Match switch
        {
            CategorisationMatch.Contains => comparable.Contains(looked, StringComparison.Ordinal),
            CategorisationMatch.StartsWith => comparable.StartsWith(looked, StringComparison.Ordinal),
            _ => comparable == looked,
        };
    }

    private static void EnsureValid(string matchText, CategorisationMatch match, int priority)
    {
        if (!IsValidMatchText(matchText))
        {
            throw new ArgumentException($"A rule's match text is required and at most {MaxMatchTextLength} characters.", nameof(matchText));
        }

        if (!Enum.IsDefined(match))
        {
            throw new ArgumentOutOfRangeException(nameof(match), match, "A rule contains, starts with or is exactly its match text.");
        }

        if (!IsValidPriority(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, $"A priority is from 0 to {MaxPriority}.");
        }
    }

    private static void GuardChoosable(Category category)
    {
        if (category.IsArchived)
        {
            throw new CategorisationRuleViolation(nameof(CategoryId), "An archived category takes nothing new: choose one that is not archived.");
        }
    }
}

/// <summary>
/// The user's categorisation rules with their categories: what picks a category for a payee or description (spec
/// IMP-4). Of the rules that match, on a category of the kind asked for that is not archived, the one of highest
/// priority wins; of equal ones, the one set up first.
/// </summary>
public sealed class Categoriser
{
    private readonly IReadOnlyList<(CategorisationRule Rule, Category Category)> _rules;

    /// <param name="rules">The user's rules.</param>
    /// <param name="categories">The user's categories, archived ones included; a rule on none of them is passed over.</param>
    public Categoriser(IEnumerable<CategorisationRule> rules, IEnumerable<Category> categories)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var byId = categories.ToDictionary(category => category.Id);
        _rules =
        [
            .. rules
                .Where(rule => byId.TryGetValue(rule.CategoryId, out var category) && !category.IsArchived)
                .OrderByDescending(rule => rule.Priority).ThenBy(rule => rule.CreatedAt).ThenBy(rule => rule.Id)
                .Select(rule => (rule, byId[rule.CategoryId])),
        ];
    }

    /// <summary>The category the winning rule gives the text; null when no rule matches it.</summary>
    /// <param name="kind">The kind of category the transaction takes; null for either.</param>
    public Guid? CategoryFor(string? text, CategoryKind? kind) =>
        _rules.Where(entry => (kind is null || entry.Category.Kind == kind) && entry.Rule.Matches(text))
            .Select(entry => (Guid?)entry.Category.Id)
            .FirstOrDefault();
}
