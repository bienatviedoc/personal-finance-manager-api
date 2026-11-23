using Pfm.Domain.Identity;

namespace Pfm.Domain.UnitTests.Identity;

public class ProfileSettingsTests
{
    [Theory]
    [InlineData("Asia/Ho_Chi_Minh")]
    [InlineData("Europe/Stockholm")]
    [InlineData("UTC")]
    public void An_IANA_time_zone_is_accepted(string timeZone)
    {
        Assert.True(ProfileSettings.IsValidTimeZone(timeZone));
        Assert.Equal(timeZone, new ProfileSettings(Currency.VND, timeZone, null).TimeZone);
    }

    [Theory]
    [InlineData("SE Asia Standard Time")]
    [InlineData("+07:00")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData(" ")]
    [InlineData("")]
    public void Anything_else_is_not_a_time_zone_and_settings_refuse_it(string timeZone)
    {
        Assert.False(ProfileSettings.IsValidTimeZone(timeZone));
        Assert.Throws<ArgumentException>(() => new ProfileSettings(Currency.VND, timeZone, null));
    }

    [Fact]
    public void A_user_is_warned_at_80_percent_of_a_budget_until_they_choose_another_threshold()
    {
        Assert.Equal(80, ProfileSettings.Default.BudgetWarningPercent);
        Assert.Equal(80, new ProfileSettings(Currency.VND, "Asia/Ho_Chi_Minh", null).BudgetWarningPercent);
        Assert.Equal(95, new ProfileSettings(Currency.VND, "Asia/Ho_Chi_Minh", null, 95).BudgetWarningPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_budget_warning_threshold_is_from_1_to_100_percent(int percent)
    {
        Assert.False(ProfileSettings.IsValidBudgetWarningPercent(percent));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProfileSettings(Currency.VND, "Asia/Ho_Chi_Minh", null, percent));
    }
}
