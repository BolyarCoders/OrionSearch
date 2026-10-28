using OrionSearch.Domain.Content;

namespace OrionSearch.Tests;

public sealed class ContentNormalizationTests
{
    [Theory]
    [InlineData("HTTPS://EXAMPLE.COM:443/Article?q=1#section", "https://example.com/Article?q=1")]
    [InlineData("http://example.com:80", "http://example.com/")]
    [InlineData("https://example.com?b=2&a=1", "https://example.com/?b=2&a=1")]
    [InlineData("https://example.com/a%2Fb/%7e?q=%2F&utm_source=x", "https://example.com/a%2Fb/%7e?q=%2F&utm_source=x")]
    [InlineData("https://example.com/path/", "https://example.com/path/")]
    [InlineData("https://example.com:8443/path", "https://example.com:8443/path")]
    [InlineData("https://[::1]:443/path", "https://[::1]/path")]
    [InlineData("https://BÜCHER.example/path", "https://xn--bcher-kva.example/path")]
    public void UrlNormalizationOnlyChangesDocumentedComponents(string input, string expected)
    {
        var normalized = CanonicalUrl.Normalize(input);

        Assert.Equal(expected, normalized);
        Assert.Equal(normalized, CanonicalUrl.Normalize(normalized));
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("/relative/path")]
    [InlineData("https://user:secret@example.com/path")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com\\path")]
    [InlineData("https://example.com/%not-valid")]
    [InlineData("https://example.com/control\u0001")]
    public void InvalidDocumentUrlsAreRejected(string input) =>
        Assert.Throws<ArgumentException>(() => CanonicalUrl.Normalize(input));

    [Theory]
    [InlineData(" EXAMPLE.COM. ", "example.com")]
    [InlineData("BÜCHER.example", "xn--bcher-kva.example")]
    public void PublisherDomainsUseLowercaseAsciiHostnames(string input, string expected) =>
        Assert.Equal(expected, DomainName.Normalize(input));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com:443")]
    public void PublisherDomainsCannotContainUrlsOrPorts(string input) =>
        Assert.Throws<ArgumentException>(() => DomainName.Normalize(input));

    [Fact]
    public void ContentHashUsesSha256OfConservativelyNormalizedText()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ContentFingerprint.Compute("abc"));
        Assert.Equal(ContentFingerprint.Compute("café\ntext"), ContentFingerprint.Compute(" cafe\u0301\r\ntext \n"));
        Assert.NotEqual(ContentFingerprint.Compute("two words"), ContentFingerprint.Compute("two  words"));
    }

    [Fact]
    public void RawContentHashUsesExactBytesWithoutTextNormalization()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ContentFingerprint.ComputeRaw("abc"u8));
        Assert.NotEqual(ContentFingerprint.ComputeRaw("abc"u8), ContentFingerprint.ComputeRaw(" abc\r\n"u8));
        Assert.NotEqual(ContentFingerprint.ComputeRaw("a\r\nb"u8), ContentFingerprint.ComputeRaw("a\nb"u8));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ContentFingerprint.ComputeRaw([]));
    }
}
