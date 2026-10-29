using System.Net;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Infrastructure.Content;

namespace OrionSearch.Tests.Research;

public sealed class CaptureStoreTests
{
    [Fact]
    public async Task ActualPythonCaptureFixtureHasCompatibleBytesTextAndUnicodeOffsets()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Contracts");
        var result = JsonSerializer.Deserialize<AgentResult>(await File.ReadAllTextAsync(Path.Combine(directory, "capture-result.json")), ResearchContract.Json)!;
        var normalized = ResearchOutputValidator.Normalize(result, result.ResearchRunId);
        var capture = Assert.Single(normalized.Sources).Capture!;
        Assert.Equal("Café 😀\nEvidence from captured bytes.", capture.ExtractedText);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "capture-source.html"));
        Assert.Equal(ContentFingerprint.ComputeRaw(bytes), capture.Raw!.Sha256);
        using var client = new StubS3(_ =>
        {
            var response = new GetObjectResponse { VersionId = "fixture-version", ContentLength = bytes.Length, ResponseStream = new MemoryStream(bytes) };
            response.Headers.ContentType = "text/html";
            return response;
        });
        Assert.Equal(bytes, (await new S3CaptureStore(client, Options).ReadAsync(capture.Raw.StorageUri, capture.Raw.Sha256, result.ResearchRunId, default)).Bytes);
    }

    private static readonly Guid Run = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly byte[] Bytes = "verified bytes"u8.ToArray();
    private static readonly string Hash = ContentFingerprint.ComputeRaw(Bytes);
    private static string Reference => $"s3://test-captures/captures/{Run:D}/{new string('a', 32)}/{Hash}?versionId=v1";
    private static CaptureStoreOptions Options => new() { Enabled = true, Bucket = "test-captures", MaxBytes = 1024 };

    [Theory]
    [InlineData("bucket")]
    [InlineData("run")]
    [InlineData("traversal")]
    [InlineData("unversioned")]
    [InlineData("null-version")]
    [InlineData("fragment")]
    [InlineData("query")]
    [InlineData("https")]
    public async Task ReferencesAreRejectedBeforeAnyNetworkAccess(string problem)
    {
        using var client = new StubS3(_ => throw new InvalidOperationException("Must not perform I/O"));
        var reference = problem switch
        {
            "bucket" => Reference.Replace("test-captures", "other-bucket"),
            "run" => Reference.Replace(Run.ToString(), Guid.NewGuid().ToString()),
            "traversal" => Reference.Replace("/captures/", "/captures/../"),
            "unversioned" => Reference.Split('?')[0],
            "null-version" => Reference.Replace("=v1", "=null"),
            "fragment" => Reference + "#fragment",
            "query" => Reference + "&credential=secret",
            _ => Reference.Replace("s3:", "https:")
        };
        Assert.Equal(ResearchFailure.Contract, (await Assert.ThrowsAsync<ResearchAgentException>(() =>
            new S3CaptureStore(client, Options).ReadAsync(reference, Hash, Run, default))).Failure);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("length")]
    [InlineData("oversized-stream")]
    [InlineData("version")]
    [InlineData("type")]
    public async Task BadBytesOrMetadataNeverEscapeVerification(string problem)
    {
        using var client = new StubS3(_ => Response(problem));
        await Assert.ThrowsAsync<ResearchAgentException>(() => new S3CaptureStore(client, Options).ReadAsync(Reference, Hash, Run, default));
    }

    [Fact]
    public async Task VersionAndDigestAreVerifiedAndStreamIsDisposed()
    {
        var response = Response("ok");
        var stream = response.ResponseStream;
        using var client = new StubS3(request =>
        {
            Assert.Equal("test-captures", request.BucketName);
            Assert.Equal("v1", request.VersionId);
            return response;
        });
        var result = await new S3CaptureStore(client, Options).ReadAsync(Reference, Hash, Run, default);
        Assert.Equal(Bytes, result.Bytes);
        Assert.Equal("text/plain", result.MediaType);
        Assert.False(stream.CanRead);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ResearchFailure.Contract)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ResearchFailure.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, ResearchFailure.Unavailable)]
    public async Task StorageFailuresHaveFiniteSafeCategories(HttpStatusCode status, ResearchFailure expected)
    {
        using var client = new StubS3(_ => throw new AmazonS3Exception("private secret body") { StatusCode = status });
        var error = await Assert.ThrowsAsync<ResearchAgentException>(() => new S3CaptureStore(client, Options).ReadAsync(Reference, Hash, Run, default));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("private", error.Message);
    }

    private static GetObjectResponse Response(string problem)
    {
        var response = new GetObjectResponse
        {
            VersionId = problem == "version" ? "other" : "v1",
            ContentLength = problem == "length" ? 100 : Bytes.Length,
            ResponseStream = new MemoryStream(problem == "digest" ? "wrong"u8.ToArray() : problem == "oversized-stream" ? new byte[1025] : Bytes)
        };
        response.Headers.ContentType = problem == "type" ? "application/javascript" : "text/plain; charset=utf-8";
        return response;
    }

    private sealed class StubS3(Func<GetObjectRequest, GetObjectResponse> get)
        : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.USEast1 })
    {
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default) => Task.FromResult(get(request));
    }
}
