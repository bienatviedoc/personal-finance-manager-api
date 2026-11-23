using System.Globalization;

namespace Pfm.Application.UseCases;

/// <summary>A calendar date as it travels in JSON and the export: <c>YYYY-MM-DD</c> (spec section 11).</summary>
public static class DateText
{
    public const string Message = "Enter a date as YYYY-MM-DD.";

    private const string Format = "yyyy-MM-dd";

    /// <summary>Null when the text was left out or is not a date written that way.</summary>
    public static DateOnly? Read(string? text) =>
        DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>The date written that way.</summary>
    public static string Of(DateOnly date) => date.ToString(Format, CultureInfo.InvariantCulture);
}
