namespace OrionSearch.Domain.Content;

public static class CanonicalUrl
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("A document URL must be an absolute HTTP(S) URL without credentials.", nameof(value));
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && (index + 2 >= value.Length
                || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])))
            {
                throw new ArgumentException("A document URL contains an invalid percent escape.", nameof(value));
            }
        }

        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        if (authorityStart < 3)
        {
            throw new ArgumentException("A document URL must include an authority.", nameof(value));
        }

        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        var suffix = authorityEnd < 0 ? "" : value[authorityEnd..];
        var fragmentStart = suffix.IndexOf('#');
        if (fragmentStart >= 0)
        {
            suffix = suffix[..fragmentStart];
        }
        if (!suffix.StartsWith('/'))
        {
            suffix = "/" + suffix;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6)
        {
            host = $"[{host.Trim('[', ']')}]";
        }
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{host}{port}{suffix}";
    }
}
