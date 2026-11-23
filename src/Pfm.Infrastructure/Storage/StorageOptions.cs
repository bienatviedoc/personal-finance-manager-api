namespace Pfm.Infrastructure.Storage;

/// <summary>
/// Settings under "Storage": the private bucket files are kept in, on any object storage that speaks the S3
/// API. Locally they point at the emulator from compose.yaml.
/// </summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>The bucket. It must exist, and must not be readable by the public.</summary>
    public string Bucket { get; set; } = "";

    /// <summary>
    /// Where the storage is, for anything but Amazon S3 itself (an emulator, another provider). Left out, it
    /// is Amazon S3 in <see cref="Region"/>.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// The address signed URLs point at, when browsers reach the storage at another address than the API does
    /// (in Compose the API calls <c>http://storage:7070</c> and the browser <c>http://localhost:7070</c>).
    /// Left out, it is <see cref="ServiceUrl"/>.
    /// </summary>
    public string? PublicUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    /// <summary>Left out with <see cref="SecretKey"/>, the credentials come from where the AWS SDK finds them (a role).</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>How long a signed URL works. Short: it is made when the user asks to open a file, and used at once.</summary>
    public TimeSpan SignedUrlLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>What is wrong with these settings, in words for whoever deploys; empty when nothing is.</summary>
    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Bucket))
        {
            yield return "Storage:Bucket is not configured. Set Storage__Bucket to the private bucket files are kept in.";
        }

        if (ServiceUrl is not null && !IsWebAddress(ServiceUrl))
        {
            yield return "Storage:ServiceUrl must be the storage's absolute address, starting http:// or https://.";
        }

        if (PublicUrl is not null && !IsWebAddress(PublicUrl))
        {
            yield return "Storage:PublicUrl must be the absolute address browsers reach the storage at, starting http:// or https://.";
        }

        if (string.IsNullOrEmpty(AccessKey) != string.IsNullOrEmpty(SecretKey))
        {
            yield return "Storage:AccessKey and Storage:SecretKey go together: set both or neither.";
        }

        if (SignedUrlLifetime <= TimeSpan.Zero || SignedUrlLifetime > TimeSpan.FromHours(1))
        {
            yield return "Storage:SignedUrlLifetime must be above zero and at most an hour: signed URLs are short-lived.";
        }
    }

    private static bool IsWebAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);
}
