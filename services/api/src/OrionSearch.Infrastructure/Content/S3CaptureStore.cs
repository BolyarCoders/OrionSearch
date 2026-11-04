using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using OrionSearch.Application.Content;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Content;

public sealed class S3CaptureStore(IAmazonS3 client, CaptureStoreOptions options) : ICaptureStore
{
    public async Task<CaptureBytes> ReadAsync(string storageUri, string sha256, Guid? runId, CancellationToken ct)
    {
        var (key, version) = Parse(storageUri, sha256, runId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var response = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = options.Bucket,
                Key = key,
                VersionId = version
            }, timeout.Token);
            if (response.VersionId != version || response.ContentLength is <= 0 || response.ContentLength > options.MaxBytes)
                throw new ResearchAgentException(ResearchFailure.Contract);
            var mediaType = response.Headers.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
            if (mediaType is not ("text/html" or "text/plain")) throw new ResearchAgentException(ResearchFailure.Contract);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            int count;
            while ((count = await response.ResponseStream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (bytes.Length + count > options.MaxBytes) throw new ResearchAgentException(ResearchFailure.Contract);
                await bytes.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            var data = bytes.ToArray();
            if (data.LongLength != response.ContentLength || ContentFingerprint.ComputeRaw(data) != sha256)
                throw new ResearchAgentException(ResearchFailure.Contract);
            return new CaptureBytes(data, mediaType);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound || exception.ErrorCode is "NoSuchKey" or "NoSuchVersion")
        { throw new ResearchAgentException(ResearchFailure.Contract); }
        catch (AmazonS3Exception) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (HttpRequestException) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (IOException) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ResearchAgentException(ResearchFailure.Unavailable); }
    }

    private (string Key, string Version) Parse(string value, string sha256, Guid? runId)
    {
        if (!options.Enabled) throw new ResearchAgentException(ResearchFailure.Unavailable);
        // Parse the literal form; URI normalization must not hide traversal or escaped paths.
        var start = $"s3://{options.Bucket}/";
        if (!value.StartsWith(start, StringComparison.Ordinal) || sha256.Length != 64
            || sha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsAsciiLetterUpper(c)))
            throw new ResearchAgentException(ResearchFailure.Contract);
        var parts = value[start.Length..].Split("?versionId=", StringSplitOptions.None);
        if (parts.Length != 2) throw new ResearchAgentException(ResearchFailure.Contract);
        var segments = parts[0].Split('/');
        if (segments.Length != 4 || segments[0] != options.Prefix || !Guid.TryParseExact(segments[1], "D", out var run)
            || runId is { } expected && run != expected || !Guid.TryParseExact(segments[2], "N", out _)
            || segments[3] != sha256 || string.IsNullOrEmpty(parts[1]) || parts[1].Length > 1024)
            throw new ResearchAgentException(ResearchFailure.Contract);
        var version = Uri.UnescapeDataString(parts[1]);
        if (version == "null" || version.Any(char.IsControl) || Uri.EscapeDataString(version) != parts[1])
            throw new ResearchAgentException(ResearchFailure.Contract);
        return (parts[0], version);
    }
}

internal sealed class DisabledCaptureStore : ICaptureStore
{
    public Task<CaptureBytes> ReadAsync(string storageUri, string sha256, Guid? runId, CancellationToken ct) =>
        throw new ResearchAgentException(ResearchFailure.Unavailable);
}
