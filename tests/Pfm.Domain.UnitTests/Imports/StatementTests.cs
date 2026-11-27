using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>
/// A statement file is read the way the user maps it (spec IMP-1): which rows to skip, which row is the header,
/// which column holds the date, the amount (or the debit and the credit) and the description, and how dates and
/// amounts are written. No bank's layout is known to the code (spec section 15).
/// </summary>
public class StatementTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    private static readonly StatementMapping Signed = new(
        Separator.Comma, HasHeaderRow: true, DateColumn: 0, DescriptionColumn: 1, AmountColumn: 2,
        DateFormat: DateFormat.DayMonthYear, DecimalSeparator: DecimalSeparator.Dot);

    private static Statement Read(string text, StatementMapping mapping, Currency currency = Currency.VND) =>
        Statement.Read(text, mapping, currency, Today);

    [Fact]
    public void Each_row_under_the_header_is_read_as_a_dated_amount_with_its_description_its_sign_saying_whether_money_came_in_or_went_out()
    {
        var statement = Read("Date,Details,Amount\n02/08/2026,SHOPEE PAY,-497000\n05/08/2026,LUONG THANG 08,28500000\n", Signed);

        Assert.Equal(["Date", "Details", "Amount"], statement.Header);
        Assert.Equal(
            [
                new StatementEntry(new DateOnly(2026, 8, 2), StatementEntryType.Expense, 497_000m, "SHOPEE PAY"),
                new StatementEntry(new DateOnly(2026, 8, 5), StatementEntryType.Income, 28_500_000m, "LUONG THANG 08"),
            ],
            statement.Entries.Select(row => row.Entry));
        Assert.Equal([2, 3], statement.Entries.Select(row => row.Number));
        Assert.Equal(StatementRowKind.Header, statement.Rows[0].Kind);
    }

    [Fact]
    public void Rows_before_the_header_and_at_the_end_are_skipped_as_the_user_says_and_blank_rows_are_passed_over()
    {
        const string text = "BANK STATEMENT,,\nAccount: 0071,,\n,,\nDate,Details,Amount\n02/08/2026,A,-1000\n,,\n03/08/2026,B,-2000\nTotal,,-3000\n\n";

        var statement = Read(text, Signed with { SkipFirstRows = 3, SkipLastRows = 1 });

        Assert.Equal(
            [StatementRowKind.Skipped, StatementRowKind.Skipped, StatementRowKind.Skipped, StatementRowKind.Header, StatementRowKind.Parsed,
             StatementRowKind.Blank, StatementRowKind.Parsed, StatementRowKind.Skipped, StatementRowKind.Blank],
            statement.Rows.Select(row => row.Kind));
        Assert.Equal(["A", "B"], statement.Entries.Select(row => row.Entry!.Description));
    }

    [Fact]
    public void Separate_debit_and_credit_columns_say_whether_money_went_out_or_came_in()
    {
        var mapping = new StatementMapping(
            Separator.Comma, HasHeaderRow: true, DateColumn: 0, DescriptionColumn: 3, DebitColumn: 1, CreditColumn: 2,
            DateFormat: DateFormat.DayMonthYear, DecimalSeparator: DecimalSeparator.Dot);

        var statement = Read("Date,Debit,Credit,Details\n02/08/2026,\"497,000\",,SHOPEE\n05/08/2026,,\"28,500,000\",SALARY\n06/08/2026,0,\"1,000\",REFUND\n", mapping);

        Assert.Equal(
            [(StatementEntryType.Expense, 497_000m), (StatementEntryType.Income, 28_500_000m), (StatementEntryType.Income, 1_000m)],
            statement.Entries.Select(row => (row.Entry!.Type, row.Entry.Amount)));
    }

    [Theory]
    [InlineData(DecimalSeparator.Dot, "1,500,000.50", "1500000.50")]
    [InlineData(DecimalSeparator.Comma, "1.500.000,50", "1500000.50")]
    [InlineData(DecimalSeparator.Comma, "-28.500.000,00", "28500000")]
    [InlineData(DecimalSeparator.Dot, "1 500 000", "1500000")]
    [InlineData(DecimalSeparator.Dot, "(2,000)", "2000")]
    [InlineData(DecimalSeparator.Dot, "2,000-", "2000")]
    [InlineData(DecimalSeparator.Dot, "2,000 VND", "2000")]
    [InlineData(DecimalSeparator.Dot, "+2,000", "2000")]
    public void Amounts_are_read_with_the_decimal_separator_the_user_chose_the_other_one_grouping_thousands(DecimalSeparator separator, string written, string amount)
    {
        var statement = Read($"Date;Details;Amount\n02/08/2026;X;{written}\n", Signed with { Separator = Separator.Semicolon, DecimalSeparator = separator }, Currency.USD);

        Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Assert.Single(statement.Entries).Entry!.Amount);
    }

    [Theory]
    [InlineData(DateFormat.DayMonthYear, "02/08/2026")]
    [InlineData(DateFormat.DayMonthYear, "2-8-2026")]
    [InlineData(DateFormat.DayMonthYear, "02.08.2026 10:22:31")]
    [InlineData(DateFormat.DayMonthYear, "02/08/26")]
    [InlineData(DateFormat.MonthDayYear, "08/02/2026")]
    [InlineData(DateFormat.YearMonthDay, "2026-08-02")]
    [InlineData(DateFormat.YearMonthDay, "2026/08/02")]
    public void Dates_are_read_in_the_order_the_user_chose_whatever_separates_their_parts(DateFormat format, string written)
    {
        var statement = Read($"Date,Details,Amount\n{written},X,-1\n", Signed with { DateFormat = format });

        Assert.Equal(new DateOnly(2026, 8, 2), Assert.Single(statement.Entries).Entry!.Date);
    }

    [Theory]
    [InlineData(",X,-1", RowProblem.NoDate)]
    [InlineData("31/02/2026,X,-1", RowProblem.DateUnreadable)]
    [InlineData("yesterday,X,-1", RowProblem.DateUnreadable)]
    [InlineData("28/11/2026,X,-1", RowProblem.DateInFuture)]
    [InlineData("02/08/2026,X,", RowProblem.NoAmount)]
    [InlineData("02/08/2026,X,0", RowProblem.ZeroAmount)]
    [InlineData("02/08/2026,X,abc", RowProblem.AmountUnreadable)]
    [InlineData("02/08/2026,X,1.2.3", RowProblem.AmountUnreadable)]
    [InlineData("02/08/2026,X,-1000.50", RowProblem.TooManyDecimals)]
    [InlineData("02/08/2026", RowProblem.MissingColumns)]
    public void A_row_that_cannot_be_read_is_kept_with_what_is_wrong_with_it(string row, RowProblem problem)
    {
        var statement = Read($"Date,Details,Amount\n{row}\n", Signed);

        var failed = Assert.Single(statement.Rows, candidate => candidate.Kind == StatementRowKind.Failed);
        Assert.Equal((2, problem), (failed.Number, failed.Problem));
        Assert.Empty(statement.Entries);
    }

    [Theory]
    [InlineData("100", "")]
    [InlineData("0", "")]
    [InlineData("100", "200")]
    public void A_debit_and_a_credit_on_one_row_or_neither_cannot_be_read(string debit, string credit)
    {
        var mapping = new StatementMapping(Separator.Comma, DateColumn: 0, DebitColumn: 1, CreditColumn: 2, DateFormat: DateFormat.DayMonthYear);

        var row = Assert.Single(Read($"02/08/2026,{debit},{credit}\n", mapping).Rows);

        Assert.Equal(
            debit == "100" && credit == "" ? StatementRowKind.Parsed : StatementRowKind.Failed,
            row.Kind);
        if (row.Kind == StatementRowKind.Failed)
        {
            Assert.Equal(credit == "" ? RowProblem.NoAmount : RowProblem.DebitAndCredit, row.Problem);
        }
    }

    [Fact]
    public void Quoted_cells_may_hold_the_separator_quotes_and_line_breaks()
    {
        var statement = Read("Date,Details,Amount\r\n02/08/2026,\"GRAB, \"\"FOOD\"\"\r\nHCM\",-1000\r\n", Signed);

        Assert.Equal("GRAB, \"FOOD\" HCM", Assert.Single(statement.Entries).Entry!.Description);
    }

    [Fact]
    public void A_description_is_trimmed_its_spaces_run_together_and_cut_to_what_a_payee_holds()
    {
        var statement = Read($"Date,Details,Amount\n02/08/2026,  A   B  ,-1\n03/08/2026,{new string('x', 300)},-1\n04/08/2026,   ,-1\n", Signed);

        Assert.Equal(["A B", new string('x', StatementEntry.MaxDescriptionLength), null], statement.Entries.Select(row => row.Entry!.Description));
    }

    [Fact]
    public void Without_the_date_and_amount_mapped_rows_are_only_cut_into_cells()
    {
        var statement = Read("Date;Details;Amount\n02/08/2026;X;-1\n", new StatementMapping(Separator.Semicolon, HasHeaderRow: true));

        Assert.False(statement.Mapping.IsComplete);
        Assert.Equal(StatementRowKind.Unmapped, statement.Rows[1].Kind);
        Assert.Equal(["02/08/2026", "X", "-1"], statement.Rows[1].Cells);
        Assert.Equal(3, statement.ColumnCount);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(2, null, null, true)]
    [InlineData(null, 2, 3, true)]
    [InlineData(null, 2, null, false)]
    [InlineData(1, 2, 3, false)]
    public void A_mapping_is_complete_with_a_date_and_either_one_amount_or_a_debit_and_a_credit(int? amount, int? debit, int? credit, bool complete) =>
        Assert.Equal(complete, new StatementMapping(Separator.Comma, DateColumn: 0, AmountColumn: amount, DebitColumn: debit, CreditColumn: credit).IsComplete);
}
