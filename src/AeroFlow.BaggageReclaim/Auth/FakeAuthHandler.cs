using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Auth;

/// <summary>
/// Development/test-only authentication (ADR-0018): mints the same claims the Entra JWT path
/// produces, so local Aspire runs and tests work offline with no tenant. Registered only when
/// <c>Auth:UseFake</c> is true (Development / tests) — never a shortcut around policies: the
/// caller still has to present the role, just as a header instead of a signed token.
/// Header shape: <c>Authorization: Fake Ops.Reclaim.Write,Other.Role</c>. No header means
/// anonymous, so authorisation still returns 401; a header without the role gives 403.
/// </summary>
public sealed class FakeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Fake";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(SchemeName + " ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var roles = header[(SchemeName.Length + 1)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var claims = new List<Claim> { new(ClaimTypes.Name, "fake-user") };
        claims.AddRange(roles.SelectMany(role => new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("roles", role),
        }));
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Fail-closed placeholder used when neither fake auth nor an Entra (<c>AzureAd</c>)
/// configuration is present: every caller is anonymous, so protected endpoints return
/// 401 rather than the app failing to start or, worse, letting anyone in.
/// </summary>
public sealed class UnavailableAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Bearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
