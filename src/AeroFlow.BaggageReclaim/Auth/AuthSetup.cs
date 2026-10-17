using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace AeroFlow.BaggageReclaim.Auth;

/// <summary>Authentication/authorisation wiring for ADR-0018: Entra ID JWT bearer in production shape, fake handler locally.</summary>
public static class AuthSetup
{
    public static IServiceCollection AddReclaimAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(AuthPolicies.ReclaimWrite, policy => policy.RequireRole(AuthRoles.ReclaimWrite)));

        if (configuration.GetValue("Auth:UseFake", false))
        {
            services.AddAuthentication(FakeAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, _ => { });
        }
        else if (!string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"]))
        {
            // Entra app roles arrive in the `roles` claim; RequireRole must look there.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.RoleClaimType = "roles");
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
        }
        else
        {
            services.AddAuthentication(UnavailableAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, UnavailableAuthHandler>(UnavailableAuthHandler.SchemeName, _ => { });
        }

        return services;
    }
}
