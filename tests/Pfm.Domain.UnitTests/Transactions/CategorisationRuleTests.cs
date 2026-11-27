using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

/// <summary>
/// A categorisation rule gives a transaction a category from its payee, or an imported row's description (spec IMP-4):
/// "description contains GRAB, Transport". When several match, the one of highest priority wins.
/// </summary>
public class CategorisationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Category Transport = Category.Create("Transport", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
    private static readonly Category EatingOut = Category.Create("Eating out", CategoryKind.Expense, "utensils", Color.Parse("#ef4444"), Now);
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "wallet", Color.Parse("#22c55e"), Now);

    private static CategorisationRule Rule(string text, CategorisationMatch match, Category category, int priority, DateTimeOffset? at = null) =>
        CategorisationRule.Create(text, match, category, priority, at ?? Now);

    private static Categoriser With(params CategorisationRule[] rules) => new(rules, [Transport, EatingOut, Salary]);

    [Theory]
    [InlineData(CategorisationMatch.Contains, "GRAB", "Grab bike HCM", true)]
    [InlineData(CategorisationMatch.Contains, "grab", "THANH TOAN GRAB", true)]
    [InlineData(CategorisationMatch.Contains, "GRAB", "Gojek", false)]
    [InlineData(CategorisationMatch.StartsWith, "grab", "GRABFOOD HCM", true)]
    [InlineData(CategorisationMatch.StartsWith, "grab", "THANH TOAN GRAB", false)]
    [InlineData(CategorisationMatch.Exactly, "grab", " GRAB ", true)]
    [InlineData(CategorisationMatch.Exactly, "grab", "GRABFOOD", false)]
    // Without accents, case or extra spaces, as a bank writes it or not.
    [InlineData(CategorisationMatch.Contains, "Phở Hòa", "PHO  HOA PASTEUR", true)]
    [InlineData(CategorisationMatch.Contains, "grab", null, false)]
    public void A_rule_matches_text_containing_starting_with_or_exactly_its_match_text_whatever_its_case_accents_or_spaces(
        CategorisationMatch match, string matchText, string? text, bool matches)
    {
        Assert.Equal(matches, Rule(matchText, match, Transport, 1).Matches(text));
    }

    [Fact]
    public void When_several_rules_match_the_one_of_highest_priority_wins()
    {
        var categoriser = With(Rule("grab", CategorisationMatch.Contains, Transport, 1), Rule("grabfood", CategorisationMatch.StartsWith, EatingOut, 5));

        Assert.Equal(EatingOut.Id, categoriser.CategoryFor("GRABFOOD HCM", CategoryKind.Expense));
        Assert.Equal(Transport.Id, categoriser.CategoryFor("GRAB BIKE", CategoryKind.Expense));
        Assert.Null(categoriser.CategoryFor("Highlands", CategoryKind.Expense));
    }

    [Fact]
    public void Of_rules_of_equal_priority_the_one_set_up_first_wins()
    {
        var categoriser = With(
            Rule("grab", CategorisationMatch.Contains, EatingOut, 3, Now.AddMinutes(1)), Rule("grab", CategorisationMatch.Contains, Transport, 3, Now));

        Assert.Equal(Transport.Id, categoriser.CategoryFor("GRAB", CategoryKind.Expense));
    }

    [Fact]
    public void A_rule_categorises_only_transactions_of_its_categorys_kind()
    {
        var categoriser = With(Rule("acme", CategorisationMatch.Contains, Salary, 1), Rule("acme", CategorisationMatch.Contains, EatingOut, 2));

        Assert.Equal(Salary.Id, categoriser.CategoryFor("ACME LTD", CategoryKind.Income));
        Assert.Equal(EatingOut.Id, categoriser.CategoryFor("ACME LTD", CategoryKind.Expense));
        // Of either kind when the kind is not known yet.
        Assert.Equal(EatingOut.Id, categoriser.CategoryFor("ACME LTD", kind: null));
    }

    [Fact]
    public void A_rule_whose_category_is_archived_or_gone_is_passed_over_for_the_next_one()
    {
        var archived = Category.Create("Taxi", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
        var gone = Category.Create("Gone", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
        CategorisationRule[] rules =
            [Rule("grab", CategorisationMatch.Contains, Transport, 1), Rule("grab", CategorisationMatch.Contains, archived, 9), Rule("grab", CategorisationMatch.Contains, gone, 8)];
        archived.Archive([], Now);
        var categoriser = new Categoriser(rules, [Transport, archived]);

        Assert.Equal(Transport.Id, categoriser.CategoryFor("GRAB", CategoryKind.Expense));
    }

    [Fact]
    public void A_rule_needs_match_text_a_known_match_type_and_a_priority_from_0_and_cannot_be_set_up_on_an_archived_category()
    {
        var archived = Category.Create("Taxi", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
        archived.Archive([], Now);

        Assert.Throws<ArgumentException>(() => Rule("  ", CategorisationMatch.Contains, Transport, 1));
        Assert.Throws<ArgumentException>(() => Rule(new string('x', CategorisationRule.MaxMatchTextLength + 1), CategorisationMatch.Contains, Transport, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule("grab", (CategorisationMatch)9, Transport, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule("grab", CategorisationMatch.Contains, Transport, -1));
        Assert.Equal(nameof(CategorisationRule.CategoryId), Assert.Throws<CategorisationRuleViolation>(() => Rule("grab", CategorisationMatch.Contains, archived, 1)).Property);
    }

    [Fact]
    public void An_edited_rule_may_keep_a_category_archived_since_but_not_move_to_one()
    {
        var taxi = Category.Create("Taxi", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
        var rule = Rule("grab", CategorisationMatch.Contains, taxi, 1);
        taxi.Archive([], Now);

        rule.Edit(" grab bike ", CategorisationMatch.StartsWith, taxi, 4, Now.AddHours(1));

        Assert.Equal(("grab bike", CategorisationMatch.StartsWith, taxi.Id, 4), (rule.MatchText, rule.Match, rule.CategoryId, rule.Priority));
        var other = Category.Create("Cabs", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
        other.Archive([], Now);
        Assert.Throws<CategorisationRuleViolation>(() => rule.Edit("grab", CategorisationMatch.Contains, other, 4, Now));
    }

    [Fact]
    public void A_rule_follows_its_category_into_the_one_it_is_merged_into()
    {
        var taxi = Category.Create("Taxi", CategoryKind.Expense, "car", Color.Parse("#3b82f6"), Now);
        var rule = Rule("grab", CategorisationMatch.Contains, taxi, 1);
        taxi.MergeInto(Transport, [], Now);

        rule.MoveCategory(taxi, Transport, Now);

        Assert.Equal(Transport.Id, rule.CategoryId);
    }
}
