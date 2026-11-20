using System.Globalization;

namespace Pfm.Api;

/// <summary>
/// How a row version travels (spec section 11): as the <c>ETag</c> of a response that answers one row, and
/// back as the <c>If-Match</c> of the edit made from it. Both are the number in double quotes.
/// </summary>
public static class RowVersions
{
    /// <summary>Reads an <c>If-Match</c> header. Anything but a quoted number is not a version the API gave out.</summary>
    public static bool TryRead(string ifMatch, out uint rowVersion)
    {
        rowVersion = default;
        return ifMatch is ['"', .. var number, '"']
            && uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out rowVersion);
    }

    /// <summary>Sends the row version as the response's <c>ETag</c>, for the next edit's <c>If-Match</c>.</summary>
    public static void SendAsETag(this HttpContext http, string rowVersion) => http.Response.Headers.ETag = $"\"{rowVersion}\"";
}
