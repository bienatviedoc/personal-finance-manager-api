using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class RecurringRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Now);
    private static readonly Category Rent = Category.Create("Rent", CategoryKind.Expense, "home", Color.Parse("#ea580c"), Now);
    private static readonly RecurringSchedule MonthlyFromThe5th = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-05"), null);

    [Fact]
    public void A_new_rule_holds_its_template_and_is_first_due_on_its_start_date()
    {
        var rule = RecurringRule.Create(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, "  Landlord ", " flat ", MonthlyFromThe5th, Now);

        Assert.Equal((TransactionType.Expense, Everyday, 9_000_000m, Rent.Id, "Landlord", "flat"), (rule.Type, rule.AccountId, rule.Amount, rule.CategoryId, rule.Payee, rule.Note));
        Assert.Equal(MonthlyFromThe5th, rule.Schedule);
        Assert.Equal(Date("2026-11-05"), rule.NextRunDate);
        Assert.False(rule.IsPaused);
        Assert.Equal(7, rule.Id.Version);
    }

    [Fact]
    public void Posting_an_occurrence_moves_the_rule_on_to_the_next_one()
    {
        var rule = PayRent(MonthlyFromThe5th);

        rule.Posted(Date("2026-11-05"), Now);
        rule.Posted(Date("2026-12-05"), Now);

        Assert.Equal(Date("2027-01-05"), rule.NextRunDate);
    }

    [Fact]
    public void Only_the_occurrence_that_is_due_next_can_be_posted()
    {
        var rule = PayRent(MonthlyFromThe5th);

        Assert.Throws<InvalidOperationException>(() => rule.Posted(Date("2026-12-05"), Now));
    }

    [Fact]
    public void A_rule_whose_last_occurrence_is_posted_is_due_no_more()
    {
        var rule = PayRent(RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-05"), Date("2026-12-31")));

        rule.Posted(Date("2026-11-05"), Now);
        rule.Posted(Date("2026-12-05"), Now);

        Assert.Null(rule.NextRunDate);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.5")]
    public void An_amount_is_greater_than_zero_and_fits_the_accounts_currency(string amount)
    {
        var violation = Assert.Throws<RecurringRuleViolation>(() => RecurringRule.Create(
            TransactionType.Expense, Everyday, Currency.VND, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Rent, null, null, MonthlyFromThe5th, Now));

        Assert.Equal(nameof(RecurringRule.Amount), violation.Property);
    }

    [Fact]
    public void An_income_rule_takes_an_income_category_and_an_expense_rule_an_expense_category()
    {
        var violation = Assert.Throws<RecurringRuleViolation>(() =>
            RecurringRule.Create(TransactionType.Income, Everyday, Currency.VND, 100m, Rent, null, null, MonthlyFromThe5th, Now));

        Assert.Equal(nameof(RecurringRule.CategoryId), violation.Property);
    }

    [Fact]
    public void A_rule_is_income_or_an_expense_and_nothing_else()
    {
        var violation = Assert.Throws<RecurringRuleViolation>(() =>
            RecurringRule.Create((TransactionType)99, Everyday, Currency.VND, 100m, Rent, null, null, MonthlyFromThe5th, Now));

        Assert.Equal(nameof(RecurringRule.Type), violation.Property);
    }

    [Fact]
    public void A_rule_takes_no_archived_category_but_keeps_one_it_already_has_through_an_edit()
    {
        var utilities = Category.Create("Utilities", CategoryKind.Expense, "zap", Color.Parse("#ea580c"), Now);
        var rule = RecurringRule.Create(TransactionType.Expense, Everyday, Currency.VND, 1m, utilities, null, null, MonthlyFromThe5th, Now);
        var archived = Category.Create("Old rent", CategoryKind.Expense, "home", Color.Parse("#ea580c"), Now);
        archived.Archive([], Now);
        utilities.Archive([], Now);

        var violation = Assert.Throws<RecurringRuleViolation>(() => rule.Edit(TransactionType.Expense, Everyday, Currency.VND, 1m, archived, null, null, MonthlyFromThe5th, Now));
        Assert.Equal(nameof(RecurringRule.CategoryId), violation.Property);
        Assert.Throws<RecurringRuleViolation>(() => RecurringRule.Create(TransactionType.Expense, Everyday, Currency.VND, 1m, archived, null, null, MonthlyFromThe5th, Now));

        rule.Edit(TransactionType.Expense, Everyday, Currency.VND, 2m, utilities, null, null, MonthlyFromThe5th, Now);
        Assert.Equal(2m, rule.Amount);
    }

    [Fact]
    public void Editing_the_schedule_never_posts_again_what_was_posted_and_carries_on_from_the_occurrence_after_it()
    {
        var rule = PayRent(MonthlyFromThe5th);
        rule.Posted(Date("2026-11-05"), Now);

        rule.Edit(TransactionType.Expense, Everyday, Currency.VND, 9_500_000m, Rent, null, null, RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-10-01"), null), Now);

        Assert.Equal(9_500_000m, rule.Amount);
        Assert.Equal(Date("2026-12-01"), rule.NextRunDate);
    }

    [Fact]
    public void Editing_a_rule_that_has_posted_nothing_takes_the_new_start_date()
    {
        var rule = PayRent(MonthlyFromThe5th);

        rule.Edit(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, null, null, RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-10-01"), null), Now);

        Assert.Equal(Date("2026-10-01"), rule.NextRunDate);
    }

    [Fact]
    public void A_later_end_date_brings_a_finished_rule_back()
    {
        var rule = PayRent(RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-05"), Date("2026-11-30")));
        rule.Posted(Date("2026-11-05"), Now);
        Assert.Null(rule.NextRunDate);

        rule.Edit(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, null, null, MonthlyFromThe5th, Now);

        Assert.Equal(Date("2026-12-05"), rule.NextRunDate);
    }

    [Fact]
    public void A_resumed_rule_skips_the_occurrences_that_were_due_while_it_was_paused_and_is_due_from_today_on()
    {
        var rule = PayRent(MonthlyFromThe5th);
        rule.Posted(Date("2026-11-05"), Now);
        rule.Pause(Now);
        Assert.True(rule.IsPaused);

        rule.Resume(today: Date("2027-02-05"), Now);

        Assert.False(rule.IsPaused);
        Assert.Equal(Date("2027-02-05"), rule.NextRunDate);
    }

    [Fact]
    public void Resuming_a_rule_that_is_not_paused_skips_nothing()
    {
        var rule = PayRent(MonthlyFromThe5th);

        rule.Resume(today: Date("2027-02-05"), Now);

        Assert.Equal(Date("2026-11-05"), rule.NextRunDate);
    }

    private static RecurringRule PayRent(RecurringSchedule schedule) =>
        RecurringRule.Create(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, "Landlord", null, schedule, Now);

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
