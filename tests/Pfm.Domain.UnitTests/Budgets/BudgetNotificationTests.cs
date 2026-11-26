using Pfm.Domain.Budgets;

namespace Pfm.Domain.UnitTests.Budgets;

/// <summary>When a category's budget raises a notification (spec BUD-7 and section 6).</summary>
public class BudgetNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth November = new(2026, 11);
    private static readonly Guid FoodId = Guid.CreateVersion7();

    [Fact]
    public void A_category_on_track_raises_nothing()
    {
        Assert.Null(BudgetNotification.Due(BudgetState.OnTrack, raised: []));
    }

    [Theory]
    [InlineData(BudgetState.Warning)]
    [InlineData(BudgetState.Over)]
    public void A_category_that_moves_into_warning_or_over_raises_a_notification_of_that_state(BudgetState state)
    {
        Assert.Equal(state, BudgetNotification.Due(state, raised: []));
    }

    [Fact]
    public void A_category_raises_one_warning_a_month_even_when_it_crosses_the_threshold_again()
    {
        Assert.Null(BudgetNotification.Due(BudgetState.Warning, raised: [BudgetState.Warning]));
    }

    [Fact]
    public void A_category_raises_one_over_a_month_even_when_it_goes_over_again()
    {
        Assert.Null(BudgetNotification.Due(BudgetState.Over, raised: [BudgetState.Warning, BudgetState.Over]));
    }

    [Fact]
    public void A_category_already_warned_about_still_raises_over_when_it_goes_over()
    {
        Assert.Equal(BudgetState.Over, BudgetNotification.Due(BudgetState.Over, raised: [BudgetState.Warning]));
    }

    [Fact]
    public void A_category_that_was_over_raises_no_warning_when_spent_drops_back_into_warning()
    {
        Assert.Null(BudgetNotification.Due(BudgetState.Warning, raised: [BudgetState.Over]));
    }

    [Fact]
    public void A_notification_keeps_what_the_budget_stood_at_when_it_was_raised_and_starts_unread()
    {
        var notification = BudgetNotification.Raise(FoodId, November, BudgetState.Warning, 3_000_000m, 2_450_000m, Currency.VND, Now);

        Assert.Equal(
            (FoodId, November, BudgetState.Warning, 3_000_000m, 2_450_000m, Currency.VND, Now, (DateTimeOffset?)null),
            (notification.CategoryId, notification.Month, notification.State, notification.Budgeted, notification.Spent, notification.Currency, notification.RaisedAt, notification.ReadAt));
    }

    [Fact]
    public void Only_warning_and_over_are_notified()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetNotification.Raise(FoodId, November, BudgetState.OnTrack, 1m, 0m, Currency.VND, Now));
    }

    [Fact]
    public void Reading_a_notification_marks_it_read_once()
    {
        var notification = BudgetNotification.Raise(FoodId, November, BudgetState.Over, 1m, 2m, Currency.VND, Now);

        notification.MarkRead(Now.AddHours(1));
        notification.MarkRead(Now.AddHours(2));

        Assert.Equal(Now.AddHours(1), notification.ReadAt);
    }
}
