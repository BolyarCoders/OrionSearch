namespace OrionSearch.Infrastructure.Content;

public sealed class CaptureStoreOptions
{
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? Bucket { get; set; }
    public string Prefix { get; set; } = "captures";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public bool AllowLoopbackHttp { get; set; }
    public int MaxBytes { get; set; } = 2097152;
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsValid() => !Enabled ||
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback && AllowLoopbackHttp)
        && !string.IsNullOrWhiteSpace(Region) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey)
        && Bucket is { Length: >= 3 and <= 63 } && Bucket.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.')
        && Prefix is { Length: >= 1 and <= 100 } && Prefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && MaxBytes is >= 1024 and <= 10485760 && TimeoutSeconds is >= 1 and <= 120;
}
