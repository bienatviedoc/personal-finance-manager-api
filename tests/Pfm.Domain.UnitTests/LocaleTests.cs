namespace Pfm.Domain.UnitTests;

public class LocaleTests
{
    [Theory]
    [InlineData("en", Locale.En)]
    [InlineData("vi", Locale.Vi)]
    public void A_locale_is_found_by_its_language_tag_and_gives_it_back(string tag, Locale expected)
    {
        Assert.True(Locales.TryParse(tag, out var locale));
        Assert.Equal(expected, locale);
        Assert.Equal(tag, locale.Tag());
    }

    [Theory]
    [InlineData("En")]
    [InlineData("vi-VN")]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_locale(string? tag)
    {
        Assert.False(Locales.TryParse(tag, out _));
    }
}
