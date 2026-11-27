using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>A statement's layout is guessed for the user to confirm or change.</summary>
public class StatementTextTests
{
    [Fact]
    public void The_separator_and_the_header_row_after_a_preamble_are_guessed_from_the_file()
    {
        const string text = "TECHCOMBANK - SAO KE\nChu tai khoan: NGUYEN VAN MAU\nKy: 01-08-2026 den 31-10-2026\n\n"
            + "Date;Remitter;Details;Amount;Balance\n05-08-2026;ABC;\"SALARY\";28.500.000,00;36.920.500,50\n05-08-2026;;\"CGV\";-179.000,00;36.741.500,50\n";

        var guessed = StatementText.SuggestMapping(text);

        Assert.Equal(new StatementMapping(Separator.Semicolon, SkipFirstRows: 4, HasHeaderRow: true), guessed);
    }

    [Fact]
    public void A_file_with_no_header_is_guessed_to_start_at_its_first_row()
    {
        var guessed = StatementText.SuggestMapping("02/08/2026,X,-1\n03/08/2026,Y,-2\n");

        Assert.Equal(new StatementMapping(Separator.Comma), guessed);
    }
}
