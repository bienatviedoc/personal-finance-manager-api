using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pfm.Domain.Imports;

namespace Pfm.Domain.UnitTests.Imports;

/// <summary>
/// Golden-file tests (spec section 13): each statement in <c>tests/statements</c> is read with the mapping a user
/// would choose for it, and every row must come out as its <c>.expected.csv</c> says. Replacing a statement means
/// replacing the file, its mapping and its expected rows; see the README beside them. With
/// <c>PFM_WRITE_GOLDEN=1</c> the expected rows are written instead of compared, to be checked by hand.
/// </summary>
public class GoldenStatementTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static TheoryData<string> Statements() => [.. StatementFiles()];

    [Fact]
    public void There_is_a_statement_of_each_bank_that_must_import_at_launch() =>
        Assert.Equal(["synthetic-stand-ins/techcombank-sample.csv", "synthetic-stand-ins/vietcombank-sample.csv"], StatementFiles().Select(path => path.Replace('\\', '/')));

    private static IEnumerable<string> StatementFiles() => Directory.GetFiles(StatementsFolder(), "*.csv", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith(".expected.csv", StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(StatementsFolder(), path))
        .Order(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Statements))]
    public void Every_row_of_a_statement_is_read_as_its_expected_rows_say(string statement)
    {
        var path = Path.Combine(StatementsFolder(), statement);
        var choices = JsonSerializer.Deserialize<Choices>(File.ReadAllText(Path.ChangeExtension(path, ".mapping.json")), Json)!;
        var text = StatementText.Decode(File.ReadAllBytes(path));
        Assert.NotNull(text);

        var read = Statement.Read(text, choices.Mapping, choices.Currency, choices.Today);

        var actual = Written(read);
        var expectedPath = Path.ChangeExtension(path, ".expected.csv");
        if (Environment.GetEnvironmentVariable("PFM_WRITE_GOLDEN") == "1")
        {
            File.WriteAllText(expectedPath, actual);
        }

        Assert.Equal(File.ReadAllText(expectedPath).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>One line per row of the statement: what it is, and what it says.</summary>
    private static string Written(Statement statement)
    {
        var written = new StringBuilder("row,kind,date,type,amount,description,problem\n");
        foreach (var row in statement.Rows)
        {
            var entry = row.Entry;
            string[] cells =
            [
                row.Number.ToString(CultureInfo.InvariantCulture),
                Name(row.Kind),
                entry?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                entry is null ? "" : Name(entry.Type),
                entry?.Amount.ToString(CultureInfo.InvariantCulture) ?? "",
                entry?.Description ?? "",
                row.Problem is { } problem ? Name(problem) : "",
            ];
            written.AppendJoin(',', cells.Select(Quoted)).Append('\n');
        }

        return written.ToString();
    }

    private static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string Quoted(string cell) =>
        cell.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : cell;

    private static string StatementsFolder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "statements"));

    /// <summary>What a user chooses on the import screen for a statement, and the account and day it is read for.</summary>
    private sealed record Choices(
        Currency Currency, DateOnly Today, Separator Separator, int SkipFirstRows, bool HasHeaderRow, int SkipLastRows, int? DateColumn,
        int? DescriptionColumn, int? AmountColumn, int? DebitColumn, int? CreditColumn, DateFormat DateFormat, DecimalSeparator DecimalSeparator)
    {
        public StatementMapping Mapping => new(
            Separator, SkipFirstRows, HasHeaderRow, SkipLastRows, DateColumn, DescriptionColumn, AmountColumn, DebitColumn, CreditColumn, DateFormat,
            DecimalSeparator);
    }
}
