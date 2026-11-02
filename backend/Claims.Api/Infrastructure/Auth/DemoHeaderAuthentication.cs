using System.Security.Claims;
using System.Text.Encodings.Web;
using Claims.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SecurityClaim = System.Security.Claims.Claim;

namespace Claims.Api.Infrastructure.Auth;

/// <summary>
/// Demo-only authentication: the caller names a demo user in the <c>X-Demo-User</c> header.
/// It produces a normal <see cref="ClaimsPrincipal"/>, so controllers use standard
/// <c>[Authorize(Roles = ...)]</c> checks and swapping in JWT bearer (OIDC) is a registration change only.
/// </summary>
public sealed class DemoHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IUserDirectory users) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DemoHeader";
    public const string HeaderName = "X-Demo-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrWhiteSpace(value))
            return Task.FromResult(AuthenticateResult.NoResult());

        var user = users.Find(value.ToString());
        if (user is null)
            return Task.FromResult(AuthenticateResult.Fail($"Unknown demo user '{value}'."));

        var identity = new ClaimsIdentity(
        [
            new SecurityClaim(ClaimTypes.NameIdentifier, user.Id),
            new SecurityClaim(ClaimTypes.Name, user.Name),
            new SecurityClaim(ClaimTypes.Email, user.Email),
            new SecurityClaim(ClaimTypes.Role, user.Role.ToString())
        ], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class AuthExtensions
{
    public static IServiceCollection AddDemoAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IUserDirectory, DemoUserDirectory>();
        services.AddAuthentication(DemoHeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DemoHeaderAuthenticationHandler>(DemoHeaderAuthenticationHandler.SchemeName, null);
        services.AddAuthorization();
        return services;
    }

    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Request is not authenticated.");
        return new Actor(
            id,
            principal.FindFirstValue(ClaimTypes.Name) ?? id,
            principal.FindFirstValue(ClaimTypes.Email) ?? "",
            Enum.Parse<UserRole>(principal.FindFirstValue(ClaimTypes.Role)!));
    }
}
