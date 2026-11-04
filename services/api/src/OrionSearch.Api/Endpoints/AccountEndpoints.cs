using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OrionSearch.Api.Authentication;
using OrionSearch.Application.Abstractions;
using OrionSearch.Domain.Auth;

namespace OrionSearch.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<NativeIdentitySettings>().Enabled) return endpoints;
        var group = endpoints.MapGroup("/auth").WithTags("Accounts").RequireRateLimiting("accounts");

        group.MapPost("/register", async (RegisterAccount request, UserManager<User> users) =>
        {
            if (!ValidEmail(request.Email) || !ValidPassword(request.Password))
                return Invalid("Provide a valid email and a password of 12–128 characters.");
            var email = request.Email.Trim();
            var name = email.Split('@', 2)[0];
            var user = new User { Email = email, DisplayName = name[..Math.Min(name.Length, 100)] };
            var result = await users.CreateAsync(user, request.Password);
            return result.Succeeded ? Results.Created("/me", Profile(user)) : RegistrationError(result);
        }).WithName("RegisterAccount").WithSummary("Create an account with email and password; no email confirmation is required")
            .Produces<AccountProfile>(201).ProducesProblem(400);

        group.MapPost("/login", async (LoginAccount request, UserManager<User> users, SignInManager<User> signIn) =>
        {
            if (!ValidEmail(request.Email) || !ValidPassword(request.Password, minimum: 1)) return Unauthorized();
            var user = await users.FindByEmailAsync(request.Email.Trim());
            // Legacy rows were never enrolled in Identity; do not interpret arbitrary preserved hashes as native credentials.
            if (user is not { Status: "active", PasswordHash: not null, NormalizedEmail: not null }) return Unauthorized();
            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded) return Unauthorized();
            return Results.SignIn(await signIn.CreateUserPrincipalAsync(user), authenticationScheme: IdentityConstants.BearerScheme);
        }).WithName("LoginAccount").WithSummary("Exchange account credentials for opaque Identity access and refresh tokens")
            .Produces<AccessTokenResponse>().ProducesProblem(401);

        group.MapPost("/refresh", async (RefreshAccount request, IOptionsMonitor<BearerTokenOptions> options,
            SignInManager<User> signIn, TimeProvider time) =>
        {
            if (string.IsNullOrEmpty(request.RefreshToken) || request.RefreshToken.Length > 16384) return Unauthorized();
            var ticket = options.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
            if (ticket?.Properties.ExpiresUtc is not { } expiry || expiry <= time.GetUtcNow()
                || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { Status: "active" } user
                || !await signIn.CanSignInAsync(user)) return Unauthorized();
            return Results.SignIn(await signIn.CreateUserPrincipalAsync(user), authenticationScheme: IdentityConstants.BearerScheme);
        }).WithName("RefreshAccount").WithSummary("Refresh a session after checking expiry, account state and security stamp")
            .Produces<AccessTokenResponse>().ProducesProblem(401);

        group.MapPost("/logout", async (ICurrentUser current, UserManager<User> users) =>
        {
            var user = await CurrentAsync(current, users);
            if (user is null) return Unauthorized();
            return (await users.UpdateSecurityStampAsync(user)).Succeeded ? Results.NoContent()
                : Results.Problem(statusCode: 409, title: "Account changed concurrently. Retry the operation.");
        }).RequireAuthorization().WithName("LogoutAccount").WithSummary("Revoke all access and refresh tokens for this account");

        group.MapPost("/change-password", async (ChangeAccountPassword request, ICurrentUser current, UserManager<User> users) =>
        {
            if (!ValidPassword(request.CurrentPassword, minimum: 1) || !ValidPassword(request.NewPassword))
                return Invalid("Provide the current password and a new password of 12–128 characters.");
            var user = await CurrentAsync(current, users);
            if (user is null) return Unauthorized();
            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            return result.Succeeded ? Results.NoContent() : Invalid("Unable to change the password.");
        }).RequireAuthorization().WithName("ChangeAccountPassword").WithSummary("Change the password and revoke existing sessions");

        endpoints.MapGet("/me", async (ICurrentUser current, UserManager<User> users) =>
        {
            var user = await CurrentAsync(current, users);
            return user is null ? Unauthorized() : Results.Ok(Profile(user));
        }).RequireAuthorization().WithTags("Accounts").WithName("GetCurrentAccount")
            .Produces<AccountProfile>().ProducesProblem(401);
        return endpoints;
    }

    private static AccountProfile Profile(User user) => new(user.Id, user.Email, user.DisplayName);
    private static bool ValidEmail(string? value) => value is { Length: > 0 and <= 254 }
        && !value.Any(char.IsControl) && new EmailAddressAttribute().IsValid(value.Trim());
    private static bool ValidPassword(string? value, int minimum = 12) => value is not null && value.Length >= minimum && value.Length <= 128;
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static IResult Unauthorized() => Results.Challenge(authenticationSchemes: [IdentityConstants.BearerScheme]);
    private static IResult RegistrationError(IdentityResult result) => result.Errors.Any(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
        ? Results.ValidationProblem(result.Errors.Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
            .GroupBy(error => error.Code).ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()))
        : Invalid("Unable to register this account.");
    private static async Task<User?> CurrentAsync(ICurrentUser current, UserManager<User> users) => current.UserId is { } id
        && await users.FindByIdAsync(id.ToString()) is { Status: "active" } user ? user : null;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterAccount(string Email, string Password);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginAccount(string Email, string Password);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefreshAccount(string RefreshToken);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeAccountPassword(string CurrentPassword, string NewPassword);
public sealed record AccountProfile(Guid Id, string Email, string DisplayName);
