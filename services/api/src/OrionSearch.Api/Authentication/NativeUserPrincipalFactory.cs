using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OrionSearch.Domain.Auth;

namespace OrionSearch.Api.Authentication;

public sealed class NativeUserPrincipalFactory(IOptions<IdentityOptions> options) : IUserClaimsPrincipalFactory<User>
{
    public Task<ClaimsPrincipal> CreateAsync(User user)
    {
        var claims = options.Value.ClaimsIdentity;
        return Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(claims.UserIdClaimType, user.Id.ToString()),
            new Claim(claims.UserNameClaimType, user.Email),
            new Claim(claims.SecurityStampClaimType, user.SecurityStamp),
            new Claim(TrustedCurrentUser.InternalUserIdClaim, user.Id.ToString())
        ], IdentityConstants.BearerScheme)));
    }
}

// Opaque bearer tickets are self-contained. Recheck the account and stamp on every request so disable/logout take effect immediately.
public sealed class NativeBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> authenticationOptions,
    ILoggerFactory logger, UrlEncoder encoder, UserManager<User> users, IOptions<IdentityOptions> options)
    : AuthenticationHandler<AuthenticationSchemeOptions>(authenticationOptions, logger, encoder)
{
    public const string SchemeName = "NativeBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await Context.AuthenticateAsync(IdentityConstants.BearerScheme);
        if (!result.Succeeded) return result;
        var principal = result.Principal!;
        var id = principal.FindAll(options.Value.ClaimsIdentity.UserIdClaimType).ToArray();
        var stamps = principal.FindAll(options.Value.ClaimsIdentity.SecurityStampClaimType).ToArray();
        if (id.Length != 1 || stamps.Length != 1 || TrustedCurrentUser.Resolve(principal)?.ToString() != id[0].Value)
            return AuthenticateResult.Fail("Invalid account identity.");
        var user = await users.FindByIdAsync(id[0].Value);
        return user is { Status: "active" } && stamps[0].Value == user.SecurityStamp
            ? AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name))
            : AuthenticateResult.Fail("Account session is no longer valid.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Context.ChallengeAsync(IdentityConstants.BearerScheme, properties);
}
