using Pfm.Domain.Transactions;

namespace Pfm.Domain.UnitTests.Transactions;

public class RecurringRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Everyday = Guid.CreateVersion7();
    private static readonly Category Salary = Category.Create("Salary", CategoryKind.Income, "briefcase", Color.Parse("#16a34a"), Now);
    private static readonly Category Rent = Category.Create("Rent", CategoryKind.Expense, "home", Color.Parse("#ea580c"), Now);
    private static readonly RecurringSchedule MonthlyFromThe5th = RecurringSchedule.Of(RecurringFrequency.Monthly, null, Date("2026-11-05"), null);
    private static readonly RecurringSchedule WeeklyFromThe2nd = RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-11-02"), null);

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

    [Fact]
    public void A_transaction_posted_by_a_rule_links_back_to_it_and_to_no_other()
    {
        var rule = PayRent(MonthlyFromThe5th);
        var transaction = Transaction.Record(
            rule.Type, rule.AccountId, Currency.VND, rule.Amount, Date("2026-11-05"), Rent, rule.Payee, rule.Note, today: Date("2026-11-16"), Now);
        Assert.Null(transaction.RecurringRuleId);

        transaction.PostedBy(rule.Id);

        Assert.Equal(rule.Id, transaction.RecurringRuleId);
        Assert.Throws<InvalidOperationException>(() => transaction.PostedBy(Guid.CreateVersion7()));
        Assert.Throws<ArgumentException>(() => transaction.PostedBy(Guid.Empty));
    }

    [Fact]
    public void A_new_rule_posts_its_occurrences_unless_it_is_set_to_remind()
    {
        Assert.Equal(RecurringMode.AutoPost, PayRent(MonthlyFromThe5th).Mode);
        Assert.Equal(RecurringMode.Remind, PayRent(MonthlyFromThe5th, RecurringMode.Remind).Mode);
    }

    [Fact]
    public void A_rule_that_reminds_has_a_reminder_for_each_occurrence_due_by_today_that_is_not_settled()
    {
        var rule = PayRent(WeeklyFromThe2nd, RecurringMode.Remind);

        Assert.Equal([Date("2026-11-02"), Date("2026-11-09"), Date("2026-11-16")], rule.Reminders(today: Date("2026-11-16")));
        Assert.Empty(PayRent(WeeklyFromThe2nd).Reminders(today: Date("2026-11-16")));
    }

    [Fact]
    public void A_paused_rule_has_no_reminders_and_no_upcoming_occurrences()
    {
        var rule = PayRent(WeeklyFromThe2nd, RecurringMode.Remind);
        rule.Pause(Now);

        Assert.Empty(rule.Reminders(today: Date("2026-11-16")));
        Assert.Empty(rule.Upcoming(today: Date("2026-11-16"), days: 30));
    }

    [Fact]
    public void Confirming_a_reminder_settles_it_and_the_next_one_is_due_next()
    {
        var rule = PayRent(WeeklyFromThe2nd, RecurringMode.Remind);

        rule.Confirmed(Date("2026-11-02"), today: Date("2026-11-10"), Now);

        Assert.Equal(Date("2026-11-09"), rule.NextRunDate);
        Assert.Equal([Date("2026-11-09")], rule.Reminders(today: Date("2026-11-10")));
    }

    [Fact]
    public void Dismissing_a_reminder_settles_it_without_a_transaction()
    {
        var rule = PayRent(WeeklyFromThe2nd, RecurringMode.Remind);

        rule.Dismiss(Date("2026-11-02"), today: Date("2026-11-10"), Now);
        rule.Dismiss(Date("2026-11-09"), today: Date("2026-11-10"), Now);

        Assert.Empty(rule.Reminders(today: Date("2026-11-10")));
        Assert.Equal(Date("2026-11-16"), rule.NextRunDate);
    }

    [Fact]
    public void Reminders_are_settled_earliest_first()
    {
        var rule = PayRent(WeeklyFromThe2nd, RecurringMode.Remind);

        var violation = Assert.Throws<RecurringRuleViolation>(() => rule.Confirmed(Date("2026-11-09"), today: Date("2026-11-10"), Now));

        Assert.Equal("DueDate", violation.Property);
        Assert.Contains("2026-11-02", violation.Message, StringComparison.Ordinal);
        Assert.Throws<RecurringRuleViolation>(() => rule.Dismiss(Date("2026-11-09"), today: Date("2026-11-10"), Now));
    }

    [Theory]
    [InlineData("2026-11-03")]
    [InlineData("2026-11-16")]
    public void Only_an_occurrence_due_by_today_on_a_rule_that_reminds_is_a_reminder(string dueDate)
    {
        var rule = PayRent(WeeklyFromThe2nd, RecurringMode.Remind);

        Assert.False(rule.HasReminder(Date(dueDate), today: Date("2026-11-10")));
        Assert.Throws<InvalidOperationException>(() => rule.Confirmed(Date(dueDate), today: Date("2026-11-10"), Now));
        Assert.False(PayRent(WeeklyFromThe2nd).HasReminder(Date("2026-11-02"), today: Date("2026-11-10")));
    }

    [Fact]
    public void Upcoming_occurrences_are_those_due_after_today_and_within_the_days_asked_for()
    {
        var rule = PayRent(WeeklyFromThe2nd);

        Assert.Equal(
            [Date("2026-11-16"), Date("2026-11-23"), Date("2026-11-30"), Date("2026-12-07")],
            rule.Upcoming(today: Date("2026-11-10"), days: 27));
    }

    [Fact]
    public void A_rule_that_ends_has_no_upcoming_occurrence_after_its_end_date()
    {
        var rule = PayRent(RecurringSchedule.Of(RecurringFrequency.Weekly, null, Date("2026-11-02"), Date("2026-11-20")));

        Assert.Equal([Date("2026-11-16")], rule.Upcoming(today: Date("2026-11-10"), days: 30));
    }

    [Fact]
    public void Editing_a_rule_sets_its_mode()
    {
        var rule = PayRent(MonthlyFromThe5th);

        rule.Edit(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, null, null, MonthlyFromThe5th, Now, RecurringMode.Remind);

        Assert.Equal(RecurringMode.Remind, rule.Mode);
    }

    private static RecurringRule PayRent(RecurringSchedule schedule, RecurringMode mode) =>
        RecurringRule.Create(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, "Landlord", null, schedule, Now, mode);

    private static RecurringRule PayRent(RecurringSchedule schedule) =>
        RecurringRule.Create(TransactionType.Expense, Everyday, Currency.VND, 9_000_000m, Rent, "Landlord", null, schedule, Now);

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
