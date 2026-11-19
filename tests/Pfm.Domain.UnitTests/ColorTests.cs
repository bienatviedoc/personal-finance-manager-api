namespace Pfm.Domain.UnitTests;

public class ColorTests
{
    [Theory]
    [InlineData("#e30613", "#e30613")]
    [InlineData("#E30613", "#e30613")]
    [InlineData("#AbCdEf", "#abcdef")]
    public void A_colour_is_written_rrggbb_and_kept_in_lower_case(string hex, string kept)
    {
        Assert.True(Color.TryParse(hex, out var color));
        Assert.Equal(kept, color.Hex);
        Assert.Equal(Color.Parse(kept), color);
    }

    [Theory]
    [InlineData("e30613")]
    [InlineData("#e3061")]
    [InlineData("#e306133")]
    [InlineData("#gggggg")]
    [InlineData("red")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_colour(string? hex)
    {
        Assert.False(Color.TryParse(hex, out _));
    }

    [Fact]
    public void Parsing_what_is_not_a_colour_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Color.Parse("green"));
    }
}
