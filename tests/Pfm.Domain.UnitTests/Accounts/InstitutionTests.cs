using Pfm.Domain.Accounts;

namespace Pfm.Domain.UnitTests.Accounts;

public class InstitutionTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    [Fact]
    public void A_new_institution_is_active_and_stamped_with_when_it_was_created()
    {
        var institution = Institution.Create("Vietcombank", null, null, Monday);

        Assert.False(institution.IsArchived);
        Assert.Equal(Monday, institution.CreatedAt);
        Assert.Equal(Monday, institution.UpdatedAt);
        Assert.Equal(7, institution.Id.Version);
    }

    [Fact]
    public void The_name_is_kept_without_surrounding_spaces_and_the_colour_in_lower_case()
    {
        var institution = Institution.Create("  Techcombank ", Color.Parse("#E30613"), "bank", Monday);

        Assert.Equal("Techcombank", institution.Name);
        Assert.Equal("#e30613", institution.Color?.Hex);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ok", "vietcombank")]
    public void An_institution_cannot_be_given_a_blank_name_or_a_logo_not_on_offer(string name, string? logoKey)
    {
        Assert.Throws<ArgumentException>(() => Institution.Create(name, null, logoKey, Monday));
    }

    [Fact]
    public void A_name_may_be_at_most_100_characters()
    {
        Assert.Equal(100, Institution.Create(new string('x', 100), null, null, Monday).Name.Length);
        Assert.Throws<ArgumentException>(() => Institution.Create(new string('x', 101), null, null, Monday));
    }

    [Fact]
    public void Editing_changes_every_field_and_when_it_was_updated()
    {
        var institution = Institution.Create("MoMo", Color.Parse("#a50064"), "wallet", Monday);

        institution.Edit("MoMo wallet", null, null, Tuesday);

        Assert.Equal(("MoMo wallet", (Color?)null, (string?)null), (institution.Name, institution.Color, institution.LogoKey));
        Assert.Equal(Monday, institution.CreatedAt);
        Assert.Equal(Tuesday, institution.UpdatedAt);
    }

    [Fact]
    public void Archiving_again_changes_nothing()
    {
        var institution = Institution.Create("Old bank", null, null, Monday);
        institution.Archive(Monday);

        institution.Archive(Tuesday);

        Assert.True(institution.IsArchived);
        Assert.Equal(Monday, institution.UpdatedAt);
    }
}
