using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OrionSearch.Application.Abstractions;

namespace OrionSearch.Api.Authentication;

public sealed class TrustedCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string InternalUserIdClaim = "orionsearch:user_id";

    public Guid? UserId => Resolve(accessor.HttpContext?.User);

    public static Guid? Resolve(ClaimsPrincipal? principal)
    {
        var claims = principal?.Identities.Where(identity => identity.IsAuthenticated)
            .SelectMany(identity => identity.FindAll(InternalUserIdClaim)).ToArray();
        return claims is { Length: 1 } && Guid.TryParse(claims[0].Value, out var id) && id != Guid.Empty ? id : null;
    }
}

// Replaced by the team's validated provider when its issuer and subject mapping are agreed.
internal sealed class UnconfiguredAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
