using System.Globalization;
using System.Text;

namespace Pfm.Domain;

/// <summary>
/// Text as two descriptions or payees are compared: without accents, in lower case, its runs of spaces made one,
/// trimmed. So "GRABFOOD  HCM" and "GrabFood HCM" are the same, as are "Phở Hòa" and "PHO HOA"; nothing else is taken out.
/// </summary>
public static class ComparableText
{
    public static string Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var plain = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(character switch { 'đ' => 'd', 'Đ' => 'D', _ => character });
            }
        }

        return string.Join(' ', plain.ToString().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
