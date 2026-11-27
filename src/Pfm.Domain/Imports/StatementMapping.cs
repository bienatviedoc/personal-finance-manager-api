namespace Pfm.Domain.Imports;

/// <summary>What separates the cells of a row in a statement file.</summary>
public enum Separator
{
    Comma = 1,
    Semicolon = 2,
    Tab = 3,
    Pipe = 4,
}

/// <summary>The order a statement writes the day, month and year of a date in, whatever separates them.</summary>
public enum DateFormat
{
    /// <summary>02/08/2026, 2-8-2026, 02.08.26.</summary>
    DayMonthYear = 1,

    /// <summary>08/02/2026.</summary>
    MonthDayYear = 2,

    /// <summary>2026-08-02, 2026/08/02.</summary>
    YearMonthDay = 3,
}

/// <summary>What a statement writes between the whole part of an amount and its decimals. The other mark groups thousands.</summary>
public enum DecimalSeparator
{
    /// <summary>1,500,000.50</summary>
    Dot = 1,

    /// <summary>1.500.000,50</summary>
    Comma = 2,
}

/// <summary>
/// How the user says a statement file is read (spec IMP-1): what separates its cells, how many rows come before
/// the one with the column names (a preamble) and after the last transaction (a footer), whether there is a header
/// row, which column holds the date, the amount (one signed amount, or a debit and a credit) and the description,
/// and how dates and amounts are written. Columns are counted from 0. Nothing here knows any bank: each bank's
/// layout is something the user maps (spec section 15).
/// </summary>
/// <param name="SkipFirstRows">Rows before the header row, or before the first transaction when there is no header row.</param>
/// <param name="SkipLastRows">Rows after the last transaction, such as a total; blank rows at the very end are not counted.</param>
/// <param name="AmountColumn">One signed amount: below zero for money going out. Left null when the statement has a debit and a credit column.</param>
/// <param name="DebitColumn">Money going out, when the statement has a column for each way.</param>
/// <param name="CreditColumn">Money coming in, when the statement has a column for each way.</param>
public sealed record StatementMapping(
    Separator Separator,
    int SkipFirstRows = 0,
    bool HasHeaderRow = false,
    int SkipLastRows = 0,
    int? DateColumn = null,
    int? DescriptionColumn = null,
    int? AmountColumn = null,
    int? DebitColumn = null,
    int? CreditColumn = null,
    DateFormat DateFormat = DateFormat.DayMonthYear,
    DecimalSeparator DecimalSeparator = DecimalSeparator.Dot)
{
    /// <summary>The most columns a mapping can name: far more than any statement has.</summary>
    public const int MaxColumn = 199;

    /// <summary>The most rows a mapping can skip at either end.</summary>
    public const int MaxSkippedRows = 1000;

    /// <summary>Whether it says enough to read a transaction from a row: a date, and either one amount or both a debit and a credit.</summary>
    public bool IsComplete =>
        DateColumn is not null
        && (AmountColumn is not null ? DebitColumn is null && CreditColumn is null : DebitColumn is not null && CreditColumn is not null);

    /// <summary>Whether every number in it is in range: nothing below zero, no column past <see cref="MaxColumn"/>.</summary>
    public bool IsInRange =>
        IsSkipInRange(SkipFirstRows) && IsSkipInRange(SkipLastRows)
        && new[] { DateColumn, DescriptionColumn, AmountColumn, DebitColumn, CreditColumn }.All(IsColumnInRange)
        && Enum.IsDefined(Separator) && Enum.IsDefined(DateFormat) && Enum.IsDefined(DecimalSeparator);

    /// <summary>Whether a mapping can skip this many rows at either end: from 0 to <see cref="MaxSkippedRows"/>, or none said.</summary>
    public static bool IsSkipInRange(int? rows) => rows is null or (>= 0 and <= MaxSkippedRows);

    /// <summary>Whether a mapping can name this column: from 0 to <see cref="MaxColumn"/>, or none.</summary>
    public static bool IsColumnInRange(int? column) => column is null or (>= 0 and <= MaxColumn);

    /// <summary>The character the separator stands for.</summary>
    public char SeparatorCharacter => Separators.Character(Separator);
}

public static class Separators
{
    public static char Character(Separator separator) => separator switch
    {
        Separator.Comma => ',',
        Separator.Semicolon => ';',
        Separator.Tab => '\t',
        Separator.Pipe => '|',
        _ => throw new ArgumentOutOfRangeException(nameof(separator), separator, "Not a separator."),
    };
}
