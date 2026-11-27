using System.Globalization;
using System.Text;

namespace Pfm.Domain.Imports;

/// <summary>Whether a row of a statement brought money in or took it out: imported as income or as an expense.</summary>
public enum StatementEntryType
{
    Income = 1,
    Expense = 2,
}

/// <summary>What a row is, once the statement is read with a mapping.</summary>
public enum StatementRowKind
{
    /// <summary>Before the header row or after the last transaction: a preamble or footer the user chose to skip.</summary>
    Skipped = 1,

    /// <summary>The row with the column names.</summary>
    Header = 2,

    /// <summary>Every cell empty: passed over.</summary>
    Blank = 3,

    /// <summary>Read as a transaction that can be imported.</summary>
    Parsed = 4,

    /// <summary>Could not be read as a transaction; shown with its <see cref="RowProblem"/> and never imported.</summary>
    Failed = 5,

    /// <summary>Cut into cells only, as the mapping does not say yet where the date and the amount are.</summary>
    Unmapped = 6,
}

/// <summary>Why a row could not be read as a transaction.</summary>
public enum RowProblem
{
    /// <summary>The row has fewer cells than the columns mapped.</summary>
    MissingColumns = 1,
    NoDate = 2,
    DateUnreadable = 3,

    /// <summary>After today in the user's time zone, which no transaction can be (spec section 5).</summary>
    DateInFuture = 4,
    NoAmount = 5,
    AmountUnreadable = 6,
    ZeroAmount = 7,

    /// <summary>More decimals than the account's currency has, or more digits than an amount holds.</summary>
    TooManyDecimals = 8,

    /// <summary>Both a debit and a credit on one row.</summary>
    DebitAndCredit = 9,
}

/// <summary>A transaction as a statement row says it: what an import creates from it.</summary>
/// <param name="Amount">Greater than zero; <paramref name="Type"/> gives the sign.</param>
/// <param name="Description">Trimmed, with its runs of spaces made one and cut to <see cref="MaxDescriptionLength"/>; null when empty.</param>
public sealed record StatementEntry(DateOnly Date, StatementEntryType Type, decimal Amount, string? Description)
{
    /// <summary>What a transaction's payee holds, which the description becomes.</summary>
    public const int MaxDescriptionLength = 200;
}

/// <summary>One row of a statement file as the mapping reads it.</summary>
/// <param name="Number">Its place in the file, counted from 1: the same row has the same number however it is mapped, as long as the separator stays.</param>
/// <param name="Cells">What the file holds in each cell, as written.</param>
/// <param name="Entry">The transaction it says, when it is <see cref="StatementRowKind.Parsed"/>.</param>
/// <param name="Problem">What is wrong with it, when it is <see cref="StatementRowKind.Failed"/>.</param>
public sealed record StatementRow(int Number, IReadOnlyList<string> Cells, StatementRowKind Kind, StatementEntry? Entry = null, RowProblem? Problem = null);

/// <summary>
/// A statement file read with a mapping: every row, with what it is, and the transaction it says when it says one.
/// Reading is the same for the preview the user checks and for the import that follows it.
/// </summary>
public sealed class Statement
{
    private Statement(StatementMapping mapping, IReadOnlyList<string> header, int columnCount, IReadOnlyList<StatementRow> rows)
    {
        Mapping = mapping;
        Header = header;
        ColumnCount = columnCount;
        Rows = rows;
    }

    public StatementMapping Mapping { get; }

    /// <summary>The cells of the header row; none when the mapping says there is no header row.</summary>
    public IReadOnlyList<string> Header { get; }

    /// <summary>The most cells any row has.</summary>
    public int ColumnCount { get; }

    public IReadOnlyList<StatementRow> Rows { get; }

    /// <summary>The rows read as transactions, in the order of the file.</summary>
    public IEnumerable<StatementRow> Entries => Rows.Where(row => row.Kind == StatementRowKind.Parsed);

    /// <param name="currency">The currency of the account the statement is imported into: its amounts may not have more decimals.</param>
    /// <param name="today">Today in the user's time zone: a row dated after it cannot be imported.</param>
    public static Statement Read(string text, StatementMapping mapping, Currency currency, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (!mapping.IsInRange)
        {
            throw new ArgumentOutOfRangeException(nameof(mapping), mapping, "The mapping skips fewer than no rows or names a column out of range.");
        }

        var records = CsvRecords.Read(text, mapping.SeparatorCharacter);
        var lastContent = records.Count - 1;
        while (lastContent >= 0 && IsBlank(records[lastContent]))
        {
            lastContent--;
        }

        var footerStart = lastContent + 1 - mapping.SkipLastRows;
        var headerAt = mapping.HasHeaderRow ? mapping.SkipFirstRows : -1;
        var rows = new List<StatementRow>(records.Count);
        IReadOnlyList<string> header = [];
        for (var index = 0; index < records.Count; index++)
        {
            var cells = records[index];
            var number = index + 1;
            if (index == headerAt)
            {
                header = cells;
                rows.Add(new StatementRow(number, cells, StatementRowKind.Header));
            }
            else if (index < mapping.SkipFirstRows || (index >= footerStart && index <= lastContent))
            {
                rows.Add(new StatementRow(number, cells, StatementRowKind.Skipped));
            }
            else if (IsBlank(cells))
            {
                rows.Add(new StatementRow(number, cells, StatementRowKind.Blank));
            }
            else if (!mapping.IsComplete)
            {
                rows.Add(new StatementRow(number, cells, StatementRowKind.Unmapped));
            }
            else
            {
                rows.Add(ReadRow(number, cells, mapping, currency, today));
            }
        }

        return new Statement(mapping, header, records.Count == 0 ? 0 : records.Max(record => record.Count), rows);
    }

    private static bool IsBlank(IReadOnlyList<string> cells) => cells.All(string.IsNullOrWhiteSpace);

    private static StatementRow ReadRow(int number, IReadOnlyList<string> cells, StatementMapping mapping, Currency currency, DateOnly today)
    {
        StatementRow Failed(RowProblem problem) => new(number, cells, StatementRowKind.Failed, Problem: problem);

        int[] mapped = [.. new[] { mapping.DateColumn, mapping.DescriptionColumn, mapping.AmountColumn, mapping.DebitColumn, mapping.CreditColumn }.OfType<int>()];
        if (mapped.Any(column => column >= cells.Count))
        {
            return Failed(RowProblem.MissingColumns);
        }

        var dateText = cells[mapping.DateColumn!.Value].Trim();
        if (dateText.Length == 0)
        {
            return Failed(RowProblem.NoDate);
        }

        if (ReadDate(dateText, mapping.DateFormat) is not { } date)
        {
            return Failed(RowProblem.DateUnreadable);
        }

        if (date > today)
        {
            return Failed(RowProblem.DateInFuture);
        }

        StatementEntryType type;
        decimal amount;
        if (mapping.AmountColumn is { } amountColumn)
        {
            var written = cells[amountColumn];
            if (string.IsNullOrWhiteSpace(written))
            {
                return Failed(RowProblem.NoAmount);
            }

            if (ReadAmount(written, mapping.DecimalSeparator) is not { } signed)
            {
                return Failed(RowProblem.AmountUnreadable);
            }

            if (signed == 0)
            {
                return Failed(RowProblem.ZeroAmount);
            }

            (type, amount) = signed < 0 ? (StatementEntryType.Expense, -signed) : (StatementEntryType.Income, signed);
        }
        else
        {
            var debit = cells[mapping.DebitColumn!.Value];
            var credit = cells[mapping.CreditColumn!.Value];
            var debitAmount = string.IsNullOrWhiteSpace(debit) ? 0 : ReadAmount(debit, mapping.DecimalSeparator);
            var creditAmount = string.IsNullOrWhiteSpace(credit) ? 0 : ReadAmount(credit, mapping.DecimalSeparator);
            if (debitAmount is null || creditAmount is null)
            {
                return Failed(RowProblem.AmountUnreadable);
            }

            (decimal Out, decimal In) moved = (Math.Abs(debitAmount.Value), Math.Abs(creditAmount.Value));
            switch (moved)
            {
                case (0, 0):
                    return Failed(RowProblem.NoAmount);
                case (not 0, not 0):
                    return Failed(RowProblem.DebitAndCredit);
                case (not 0, 0):
                    (type, amount) = (StatementEntryType.Expense, moved.Out);
                    break;
                default:
                    (type, amount) = (StatementEntryType.Income, moved.In);
                    break;
            }
        }

        if (!Amounts.IsValid(amount, currency))
        {
            return Failed(RowProblem.TooManyDecimals);
        }

        var description = mapping.DescriptionColumn is { } descriptionColumn ? Describe(cells[descriptionColumn]) : null;
        return new StatementRow(number, cells, StatementRowKind.Parsed, new StatementEntry(date, type, amount, description));
    }

    /// <summary>The first three runs of digits, in the order the format gives; anything after them (a time) is passed over.</summary>
    private static DateOnly? ReadDate(string text, DateFormat format)
    {
        var parts = new List<int>(3);
        for (var at = 0; at < text.Length && parts.Count < 3;)
        {
            if (!char.IsAsciiDigit(text[at]))
            {
                at++;
                continue;
            }

            var start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            if (at - start > 4)
            {
                return null;
            }

            parts.Add(int.Parse(text.AsSpan(start, at - start), CultureInfo.InvariantCulture));
        }

        if (parts.Count < 3 || text.TakeWhile(character => !char.IsAsciiDigit(character)).Any(char.IsLetter))
        {
            return null;
        }

        var (year, month, day) = format switch
        {
            DateFormat.DayMonthYear => (parts[2], parts[1], parts[0]),
            DateFormat.MonthDayYear => (parts[2], parts[0], parts[1]),
            _ => (parts[0], parts[1], parts[2]),
        };
        if (year < 100)
        {
            year += 2000;
        }

        return year is >= 1900 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;
    }

    /// <summary>
    /// A signed amount: one well-formed number, written with the decimal separator chosen and the other mark (or a
    /// space) grouping thousands in threes. One sign before it or after it, or brackets round it, make it negative;
    /// a plus sign may stand before or after it. Anything else, a second sign, a letter, a currency, an exponent, is
    /// no amount: null.
    /// </summary>
    private static decimal? ReadAmount(string text, DecimalSeparator decimalSeparator)
    {
        var (decimalMark, groupMark) = decimalSeparator == DecimalSeparator.Comma ? (',', '.') : ('.', ',');
        var number = text.Trim();
        var negative = false;
        if (number.Length >= 2 && number[0] == '(' && number[^1] == ')')
        {
            (negative, number) = (true, number[1..^1].Trim());
        }
        else if (number.Length > 0 && number[0] is '-' or '+')
        {
            (negative, number) = (number[0] == '-', number[1..].TrimStart());
        }
        else if (number.Length > 0 && number[^1] is '-' or '+')
        {
            (negative, number) = (number[^1] == '-', number[..^1].TrimEnd());
        }

        var parts = number.Split(decimalMark);
        if (parts.Length > 2 || (parts.Length == 2 && !IsDigits(parts[1])) || Whole(parts[0], groupMark) is not { } whole)
        {
            return null;
        }

        var digits = parts.Length == 2 ? $"{whole}.{parts[1]}" : whole;
        return decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? negative ? -amount : amount
            : null;
    }

    /// <summary>The digits of a whole number, written plain or grouped in threes by the group mark or a space (a no-break one too); null when it is neither.</summary>
    private static string? Whole(string written, char groupMark)
    {
        var groups = written.Split([groupMark, ' ', '\u00A0', '\u202F']);
        return groups.All(IsDigits) && (groups.Length == 1 || (groups[0].Length <= 3 && groups.Skip(1).All(group => group.Length == 3)))
            ? string.Concat(groups)
            : null;
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private static string? Describe(string cell)
    {
        var words = cell.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var description = string.Join(' ', words);
        return description.Length <= StatementEntry.MaxDescriptionLength ? description : description[..StatementEntry.MaxDescriptionLength].TrimEnd();
    }
}
