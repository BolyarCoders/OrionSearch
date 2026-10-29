using System.Net;
using Microsoft.Extensions.Options;

namespace OrionSearch.Api.Configuration;

public static class RateLimitClientAddress
{
    public static string Get(HttpContext context)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<ReverseProxySettings>>().Value;
        if (!settings.TrustRenderEdge)
            return Normalize(context.Connection.RemoteIpAddress);

        // Render Free web services accept only public traffic through Render's edge.
        // Cloudflare replaces this single header; never use the caller's X-Forwarded-For chain.
        var values = context.Request.Headers["CF-Connecting-IP"];
        return values.Count == 1 && IPAddress.TryParse(values[0], out var address)
            ? Normalize(address) : "unknown";
    }

    private static string Normalize(IPAddress? address) =>
        address is null ? "unknown" : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
