using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>
/// A row matching an existing transaction on account, date, amount and normalised description is a likely
/// duplicate (spec IMP-3): flagged, never silently dropped.
/// </summary>
public class LikelyDuplicateTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    private static readonly StatementMapping Mapping = new(
        Separator.Comma, DateColumn: 0, DescriptionColumn: 1, AmountColumn: 2, DateFormat: DateFormat.DayMonthYear);

    [Fact]
    public void A_row_with_the_date_amount_and_description_of_an_existing_transaction_is_a_likely_duplicate()
    {
        var statement = Statement.Read("02/08/2026,GRABFOOD HCM,-146000\n02/08/2026,GRABFOOD HCM,-150000\n03/08/2026,GRABFOOD HCM,-146000\n02/08/2026,SHOPEE,-146000\n02/08/2026,GRABFOOD HCM,146000\n", Mapping, Currency.VND, Today);

        var duplicates = LikelyDuplicates.Among(statement, [new ExistingEntry(new DateOnly(2026, 8, 2), StatementEntryType.Expense, 146_000m, "GRABFOOD HCM")]);

        Assert.Equal([1], duplicates);
    }

    [Theory]
    [InlineData("  grabfood   hcm ")]
    [InlineData("GrabFood HCM")]
    [InlineData("GRÁBFOOD HCM")]
    public void Descriptions_match_whatever_their_case_spacing_and_accents(string existing)
    {
        var statement = Statement.Read("02/08/2026,GRABFOOD HCM,-146000\n", Mapping, Currency.VND, Today);

        Assert.Equal([1], LikelyDuplicates.Among(statement, [new ExistingEntry(new DateOnly(2026, 8, 2), StatementEntryType.Expense, 146_000m, existing)]));
    }

    [Fact]
    public void Each_existing_transaction_makes_one_row_a_likely_duplicate_so_two_coffees_on_a_day_against_one_recorded_flag_only_one()
    {
        var statement = Statement.Read("02/08/2026,HIGHLANDS,-55000\n02/08/2026,HIGHLANDS,-55000\n", Mapping, Currency.VND, Today);

        Assert.Equal([1], LikelyDuplicates.Among(statement, [new ExistingEntry(new DateOnly(2026, 8, 2), StatementEntryType.Expense, 55_000m, "Highlands")]));
    }

    [Fact]
    public void An_empty_description_matches_an_existing_transaction_with_none()
    {
        var statement = Statement.Read("02/08/2026,,-55000\n", Mapping, Currency.VND, Today);

        Assert.Equal([1], LikelyDuplicates.Among(statement, [new ExistingEntry(new DateOnly(2026, 8, 2), StatementEntryType.Expense, 55_000m, null)]));
    }
}
