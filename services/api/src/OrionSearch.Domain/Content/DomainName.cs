using System.Globalization;

namespace OrionSearch.Domain.Content;

public static class DomainName
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var domain = new IdnMapping().GetAscii(value.Trim().TrimEnd('.')).ToLowerInvariant();
        if (Uri.CheckHostName(domain) != UriHostNameType.Dns)
        {
            throw new ArgumentException("A publisher domain must be a DNS hostname without a scheme, port or path.", nameof(value));
        }
        return domain;
    }
}
