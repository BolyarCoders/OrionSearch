using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrionSearch.Api.Authentication;

namespace OrionSearch.Tests;

public sealed class ArchiveAuthenticationTests
{
    [Theory]
    [InlineData("POST", "/archives")]
    [InlineData("GET", "/archives")]
    [InlineData("GET", "/archives/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/archives/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/archives/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/archives/00000000-0000-0000-0000-000000000001/sources")]
    [InlineData("POST", "/archives/00000000-0000-0000-0000-000000000001/research-jobs")]
    [InlineData("GET", "/research-jobs/00000000-0000-0000-0000-000000000001")]
    public async Task NormalStartupRejectsCallerIdentityHeadersAndUnvalidatedBearerTokens(string method, string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { title = "Spoof" }) };
        request.Headers.Add("X-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Identity", Guid.NewGuid().ToString());
        request.Headers.Authorization = new("Bearer", "unvalidated-token");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void CurrentUserRequiresOneInternalUuidClaimOnAnAuthenticatedIdentity()
    {
        var id = Guid.NewGuid();
        var context = new DefaultHttpContext();
        var current = new TrustedCurrentUser(new HttpContextAccessor { HttpContext = context });
        context.User = Principal([new(TrustedCurrentUser.InternalUserIdClaim, id.ToString())], null);
        Assert.Null(current.UserId);
        context.User = Principal([new(ClaimTypes.NameIdentifier, id.ToString())], "Provider");
        Assert.Null(current.UserId);
        context.User = Principal([new(TrustedCurrentUser.InternalUserIdClaim, id.ToString())], "Provider");
        Assert.Equal(id, current.UserId);
        context.User.AddIdentity(new ClaimsIdentity([new Claim(TrustedCurrentUser.InternalUserIdClaim, Guid.NewGuid().ToString())], "OtherProvider"));
        Assert.Null(current.UserId);
        context.User = Principal([new(TrustedCurrentUser.InternalUserIdClaim, "not-a-uuid")], "Provider");
        Assert.Null(current.UserId);
        context.User = Principal([new(TrustedCurrentUser.InternalUserIdClaim, Guid.Empty.ToString())], "Provider");
        Assert.Null(current.UserId);
    }

    private static ClaimsPrincipal Principal(Claim[] claims, string? authenticationType) => new(new ClaimsIdentity(claims, authenticationType));
}
