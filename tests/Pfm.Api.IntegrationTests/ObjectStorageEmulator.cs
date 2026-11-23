using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Private object storage for the tests: the same S3 emulator the local stack runs (compose.yaml), in a
/// container, with one empty bucket. It checks signatures as real storage does, so a signed URL is
/// tested for real.
/// </summary>
public sealed class ObjectStorageEmulator : IAsyncDisposable
{
    private const int Port = 7070;
    private const string Bucket = "pfm-test-attachments";
    private const string AccessKey = "pfm-test";
    private const string SecretKey = "pfm-test-secret-key";

    private readonly IContainer _container = new ContainerBuilder("versity/versitygw:v1.11.0")
        .WithEnvironment("ROOT_ACCESS_KEY", AccessKey)
        .WithEnvironment("ROOT_SECRET_KEY", SecretKey)
        .WithCommand("posix", "/data")
        .WithTmpfsMount("/data")
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(Port))
        .Build();

    /// <summary>The address of the storage, as the API and anyone holding a signed URL reach it.</summary>
    public Uri Url => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}");

    /// <summary>The settings that point a copy of the API at this storage.</summary>
    public Dictionary<string, string> Settings => new()
    {
        ["Storage:ServiceUrl"] = Url.ToString(),
        ["Storage:Bucket"] = Bucket,
        ["Storage:AccessKey"] = AccessKey,
        ["Storage:SecretKey"] = SecretKey,
    };

    public async Task StartAsync()
    {
        await _container.StartAsync();
        using var s3 = new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = Url.ToString(), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await s3.PutBucketAsync(Bucket);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
