using System.Globalization;
using System.Text;

namespace Pfm.Domain.Imports;

/// <summary>A transaction already on the account a statement is imported into, as a likely duplicate is matched against it.</summary>
/// <param name="Type">Which way money moved: a transfer out is money going out, a transfer in money coming in.</param>
/// <param name="Description">Its payee, which an imported row's description becomes.</param>
/// <param name="IsTransferLeg">
/// One leg of a transfer. A leg has no payee (marking two transactions as a transfer drops theirs), so it matches a
/// row on the date, amount and direction alone, whatever the row's description.
/// </param>
public sealed record ExistingEntry(DateOnly Date, StatementEntryType Type, decimal Amount, string? Description, bool IsTransferLeg = false);

/// <summary>
/// Finds the rows of a statement that are likely duplicates (spec IMP-3): a row on the same date, of the same
/// amount the same way, with the same normalised description as a transaction already on the account, or on the same
/// date, of the same amount the same way as a leg of a transfer on it, which has no description to compare. Each
/// existing transaction accounts for one row, so two coffees on a day against one recorded flag only one; a row is
/// matched against income and expenses before transfer legs.
/// A likely duplicate is flagged and left out unless the user ticks it, never dropped silently.
/// </summary>
public static class LikelyDuplicates
{
    /// <summary>The numbers of the rows that are likely duplicates, earliest in the file first.</summary>
    public static IReadOnlySet<int> Among(Statement statement, IEnumerable<ExistingEntry> existing)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var entries = existing.ToList();
        var unmatched = entries.Where(entry => !entry.IsTransferLeg)
            .GroupBy(entry => Key(entry.Date, entry.Type, entry.Amount, entry.Description))
            .ToDictionary(group => group.Key, group => group.Count());
        var unmatchedLegs = entries.Where(entry => entry.IsTransferLeg)
            .GroupBy(entry => Key(entry.Date, entry.Type, entry.Amount, description: null))
            .ToDictionary(group => group.Key, group => group.Count());

        var duplicates = new SortedSet<int>();
        foreach (var row in statement.Entries)
        {
            var entry = row.Entry!;
            if (Take(unmatched, Key(entry.Date, entry.Type, entry.Amount, entry.Description))
                || Take(unmatchedLegs, Key(entry.Date, entry.Type, entry.Amount, description: null)))
            {
                duplicates.Add(row.Number);
            }
        }

        return duplicates;
    }

    private static bool Take(Dictionary<(DateOnly, StatementEntryType, decimal, string), int> unmatched, (DateOnly, StatementEntryType, decimal, string) key)
    {
        if (!unmatched.TryGetValue(key, out var left) || left == 0)
        {
            return false;
        }

        unmatched[key] = left - 1;
        return true;
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
