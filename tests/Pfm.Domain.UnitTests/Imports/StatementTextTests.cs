using System.Text;
using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>An uploaded file is read as text, and its layout is guessed for the user to confirm or change.</summary>
public class StatementTextTests
{
    [Fact]
    public void UTF8_with_or_without_a_byte_order_mark_and_UTF16_with_one_are_read_as_text()
    {
        const string text = "Ngày;Số tiền\n";

        Assert.Equal(text, StatementText.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]));
        Assert.Equal(text, StatementText.Decode(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, StatementText.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]));
    }

    [Fact]
    public void A_file_that_is_not_text_is_not_read()
    {
        Assert.Null(StatementText.Decode([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]));
        Assert.Null(StatementText.Decode([0x4E, 0x67, 0xE0, 0x79]));
        Assert.Null(StatementText.Decode([]));
    }

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
