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
}
