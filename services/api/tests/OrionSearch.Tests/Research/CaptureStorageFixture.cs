using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OrionSearch.Domain.Content;
using OrionSearch.Infrastructure.Content;
using OrionSearch.Application.Research;

namespace OrionSearch.Tests.Research;

public sealed class CaptureStorageFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("chrislusf/seaweedfs:4.51")
        .WithCommand("server", "-s3", "-dir=/tmp", "-ip=127.0.0.1", "-ip.bind=0.0.0.0", "-volume.max=16", "-master.volumeSizeLimitMB=32")
        .WithPortBinding(8333, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8333).ForPath("/"),
            strategy => strategy.WithTimeout(TimeSpan.FromMinutes(2))))
        .Build();
    public IAmazonS3 Client { get; private set; } = null!;
    public CaptureStoreOptions Options { get; } = new() { Enabled = true, Bucket = "test-captures", AllowLoopbackHttp = true, AccessKey = "local-test", SecretKey = "local-test" };
    public S3CaptureStore Store => new(Client, Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Options.Endpoint = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8333)}";
        Client = new AmazonS3Client(new BasicAWSCredentials("local-test", "local-test"), new AmazonS3Config
        {
            ServiceURL = Options.Endpoint,
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
            MaxErrorRetry = 0
        });
        await Client.PutBucketAsync(new PutBucketRequest { BucketName = Options.Bucket });
        await Client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = Options.Bucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
    }

    public async Task<RawCaptureOutput> UploadAsync(Guid run, byte[] bytes, string mediaType = "text/plain")
    {
        var hash = ContentFingerprint.ComputeRaw(bytes);
        var key = $"captures/{run:D}/{Guid.NewGuid():N}/{hash}";
        using var stream = new MemoryStream(bytes);
        var response = await Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Options.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = mediaType,
            IfNoneMatch = "*"
        });
        Assert.False(string.IsNullOrEmpty(response.VersionId));
        Assert.NotEqual("null", response.VersionId);
        return new RawCaptureOutput($"s3://{Options.Bucket}/{key}?versionId={Uri.EscapeDataString(response.VersionId)}", hash);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}
