using System.Globalization;

namespace Pfm.Application.UseCases;

/// <summary>Reads a calendar date as it travels in JSON: <c>YYYY-MM-DD</c> (spec section 11).</summary>
public static class DateText
{
    public const string Message = "Enter a date as YYYY-MM-DD.";

    /// <summary>Null when the text was left out or is not a date written that way.</summary>
    public static DateOnly? Read(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
