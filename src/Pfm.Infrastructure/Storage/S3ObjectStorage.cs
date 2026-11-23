using System.Net.Http.Headers;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Pfm.Application.Abstractions;

namespace Pfm.Infrastructure.Storage;

/// <summary>
/// Object storage over the S3 API (decision record 0004): Amazon S3, or anything that speaks it. The bucket
/// is private; a file leaves it only through a URL signed here, which needs no call to the storage.
/// </summary>
internal sealed class S3ObjectStorage(IOptions<StorageOptions> options, TimeProvider clock) : IObjectStorage, IDisposable
{
    private readonly StorageOptions _options = options.Value;
    private readonly AmazonS3Client _storage = ClientFor(options.Value, options.Value.ServiceUrl);

    /// <summary>Signs URLs for the address browsers use; the signature covers the host, so it cannot be swapped afterwards.</summary>
    private readonly AmazonS3Client _signer = ClientFor(options.Value, options.Value.PublicUrl ?? options.Value.ServiceUrl);

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
        await _storage.PutObjectAsync(
            new PutObjectRequest { BucketName = _options.Bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false },
            cancellationToken);

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await _storage.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _options.Bucket, Key = key }, cancellationToken);

    public SignedUrl SignedUrlFor(string key, string fileName)
    {
        var expiresAt = clock.GetUtcNow() + _options.SignedUrlLifetime;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,

            // The SDK signs for HTTPS unless told otherwise; the local emulator is plain HTTP.
            Protocol = PublicAddressIsPlainHttp ? Protocol.HTTP : Protocol.HTTPS,
        };

        // Shown in the browser, under the name the user gave the file.
        request.ResponseHeaderOverrides.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = fileName }.ToString();
        return new SignedUrl(new Uri(_signer.GetPreSignedURL(request)), expiresAt);
    }

    private bool PublicAddressIsPlainHttp =>
        (_options.PublicUrl ?? _options.ServiceUrl) is { } url && new Uri(url).Scheme == Uri.UriSchemeHttp;

    public void Dispose()
    {
        _storage.Dispose();
        _signer.Dispose();
    }

    private static AmazonS3Client ClientFor(StorageOptions options, string? url)
    {
        var config = new AmazonS3Config();
        if (url is null)
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            // Another provider or an emulator: one address, the bucket in the path, and no checksum
            // trailers, which not every one of them reads.
            config.ServiceURL = url;
            config.AuthenticationRegion = options.Region;
            config.ForcePathStyle = true;
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }

        return string.IsNullOrEmpty(options.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }
}
