namespace Pfm.Infrastructure.Persistence;

/// <summary>Text as an ILIKE pattern that matches it literally: its own wildcards (%, _) match only themselves.</summary>
internal static class LikePattern
{
    /// <summary>The escape character the patterns below are written with, to pass to ILIKE beside them.</summary>
    public const string Escape = "\\";

    /// <summary>The text itself, in any case, never a pattern.</summary>
    public static string Exactly(string text) =>
        text.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

    /// <summary>Any text that contains the text, in any case.</summary>
    public static string Containing(string text) => $"%{Exactly(text)}%";
}
