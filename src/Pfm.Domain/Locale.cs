namespace Pfm.Domain;

/// <summary>
/// The language a user uses the app in: the interface, how numbers, amounts and dates are written, and
/// the names of the default categories they start with. Its tag is the BCP 47 language tag: <c>en</c> or <c>vi</c>.
/// </summary>
public enum Locale
{
    En,
    Vi,
}

public static class Locales
{
    public static string Tag(this Locale locale) => locale switch
    {
        Locale.En => "en",
        Locale.Vi => "vi",
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null),
    };

    /// <summary>The locale with exactly this tag.</summary>
    public static bool TryParse(string? tag, out Locale locale)
    {
        foreach (var candidate in Enum.GetValues<Locale>())
        {
            if (candidate.Tag() == tag)
            {
                locale = candidate;
                return true;
            }
        }

        locale = default;
        return false;
    }
}
