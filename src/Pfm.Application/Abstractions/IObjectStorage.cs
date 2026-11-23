namespace Pfm.Application.Abstractions;

/// <summary>
/// Private object storage, where files live (spec section 8). Nothing in it can be read without a signed
/// URL, and the browser never gets anything else.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Stores a file under <paramref name="key"/>, replacing what was there.</summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Removes the file under <paramref name="key"/>; nothing happens when there is none.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// A URL that reads the file under <paramref name="key"/> for a few minutes, with no sign-in, and
    /// then stops working. The browser shows the file under <paramref name="fileName"/>.
    /// </summary>
    SignedUrl SignedUrlFor(string key, string fileName);
}

/// <param name="ExpiresAt">When the URL stops working.</param>
public sealed record SignedUrl(Uri Url, DateTimeOffset ExpiresAt);
