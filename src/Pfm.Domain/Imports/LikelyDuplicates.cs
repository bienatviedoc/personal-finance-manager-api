using System.Globalization;
using System.Text;

namespace Pfm.Domain.Imports;

/// <summary>A transaction already on the account a statement is imported into, as a likely duplicate is matched against it.</summary>
/// <param name="Description">Its payee, which an imported row's description becomes.</param>
public sealed record ExistingEntry(DateOnly Date, StatementEntryType Type, decimal Amount, string? Description);

/// <summary>
/// Finds the rows of a statement that are likely duplicates (spec IMP-3): a row on the same date, of the same
/// amount the same way, with the same normalised description as a transaction already on the account. Each
/// existing transaction accounts for one row, so two coffees on a day against one recorded flag only one.
/// A likely duplicate is flagged and left out unless the user ticks it, never dropped silently.
/// </summary>
public static class LikelyDuplicates
{
    /// <summary>The numbers of the rows that are likely duplicates, earliest in the file first.</summary>
    public static IReadOnlySet<int> Among(Statement statement, IEnumerable<ExistingEntry> existing)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var unmatched = existing
            .GroupBy(entry => Key(entry.Date, entry.Type, entry.Amount, entry.Description))
            .ToDictionary(group => group.Key, group => group.Count());

        var duplicates = new SortedSet<int>();
        foreach (var row in statement.Entries)
        {
            var entry = row.Entry!;
            var key = Key(entry.Date, entry.Type, entry.Amount, entry.Description);
            if (unmatched.TryGetValue(key, out var left) && left > 0)
            {
                unmatched[key] = left - 1;
                duplicates.Add(row.Number);
            }
        }

        return duplicates;
    }

    /// <summary>
    /// A description as it is compared: without accents, in lower case, its runs of spaces made one, trimmed.
    /// So "GRABFOOD  HCM" and "GrabFood HCM" are the same; nothing else is taken out.
    /// </summary>
    public static string Normalise(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        var plain = new StringBuilder(description.Length);
        foreach (var character in description.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(character switch { 'đ' => 'd', 'Đ' => 'D', _ => character });
            }
        }

        return string.Join(' ', plain.ToString().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static (DateOnly, StatementEntryType, decimal, string) Key(DateOnly date, StatementEntryType type, decimal amount, string? description) =>
        (date, type, amount, Normalise(description));
}
