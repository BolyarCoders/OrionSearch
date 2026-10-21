namespace OrionSearch.Api.Configuration;

public sealed class ReverseProxySettings
{
    public const string SectionName = "ReverseProxy";

    public string[] KnownProxies { get; init; } = [];
}
