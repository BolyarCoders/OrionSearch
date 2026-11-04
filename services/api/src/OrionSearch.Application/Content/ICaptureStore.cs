namespace OrionSearch.Application.Content;

public sealed record CaptureBytes(byte[] Bytes, string MediaType);

public interface ICaptureStore
{
    // A read must return only digest-verified bytes from an immutable version.
    Task<CaptureBytes> ReadAsync(string storageUri, string sha256, Guid? runId, CancellationToken ct);
}
