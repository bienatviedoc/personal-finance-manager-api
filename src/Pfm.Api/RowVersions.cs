using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Pfm.Api;

/// <summary>
/// How a row version travels (spec section 11): as the <c>ETag</c> of a response that answers one row, and
/// back as the <c>If-Match</c> of the edit made from it. Both are the number in double quotes.
/// </summary>
public static class RowVersions
{
    /// <summary>
    /// Reads the <c>If-Match</c> of an edit. Without one the edit is refused with 428; anything but a quoted
    /// number is not a version the API gave out, so it matches nothing and the edit is stale (412).
    /// </summary>
    /// <param name="what">What is being edited, as the problem's title names it: "account".</param>
    public static bool TryRead(string? ifMatch, string what, out uint rowVersion, [NotNullWhen(false)] out ProblemHttpResult? refused)
    {
        rowVersion = default;
        refused = ifMatch switch
        {
            null => Required(what),
            ['"', .. var number, '"'] when uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out rowVersion) => null,
            _ => Stale(what),
        };

        return refused is null;
    }

    /// <summary>
    /// Reads the <c>If-Match</c> of a request that sets something which may not exist yet, so has no row
    /// version to name: without one <paramref name="rowVersion"/> is null. One that is sent is read as
    /// <see cref="TryRead"/> reads it.
    /// </summary>
    public static bool TryReadOptional(string? ifMatch, string what, out uint? rowVersion, [NotNullWhen(false)] out ProblemHttpResult? refused)
    {
        (rowVersion, refused) = (null, null);
        if (ifMatch is null)
        {
            return true;
        }

        var read = TryRead(ifMatch, what, out var sent, out refused);
        rowVersion = read ? sent : null;
        return read;
    }

    /// <summary>The 428 an edit that names no row version answers; nothing was saved.</summary>
    public static ProblemHttpResult Required(string what) =>
        TypedResults.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: $"Send the {what}'s row version as If-Match.");

    /// <summary>The 412 an edit made from a row version that is no longer the row's answers; nothing was saved.</summary>
    public static ProblemHttpResult Stale(string what) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed, title: $"The {what} has changed since it was read. Read it again, then edit it.");

    /// <summary>Sends the row version as the response's <c>ETag</c>, for the next edit's <c>If-Match</c>.</summary>
    public static void SendAsETag(this HttpContext http, string rowVersion) => http.Response.Headers.ETag = $"\"{rowVersion}\"";
}
