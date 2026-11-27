using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;

namespace LedgerCore.Admin;

/// <summary>Sign-in through any OpenID Connect provider; pages check its roles.</summary>
public static class BackOfficeAuthentication
{
    public const string SectionName = "Oidc";
    public const string RolesClaim = "roles";

    public static IServiceCollection AddBackOfficeAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(SectionName).Get<OidcSettings>()
            ?? throw new InvalidOperationException($"The back office needs an '{SectionName}' section with Authority and ClientId.");
        settings.Validate();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.AccessDeniedPath = "/access-denied";
                options.Cookie.Name = "__Host-ledger-admin";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;

                // keep the provider's claim names, so "roles" stays "roles" instead of a schema URI
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = RolesClaim;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(BackOfficePolicies.Viewer, policy => policy.RequireRole(BackOfficeRoles.Viewer, BackOfficeRoles.Operator))
            .AddPolicy(BackOfficePolicies.Operator, policy => policy.RequireRole(BackOfficeRoles.Operator))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddCascadingAuthenticationState();
        return services;
    }

    public static IEndpointRouteBuilder MapSignOut(this IEndpointRouteBuilder endpoints)
    {
        // minimal APIs only enforce antiforgery when they bind a form, and this one binds nothing
        endpoints.MapPost("/sign-out", async (HttpContext context, IAntiforgery antiforgery) =>
            await antiforgery.IsRequestValidAsync(context)
                ? Results.SignOut(
                    new AuthenticationProperties { RedirectUri = "/" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])
                : Results.BadRequest());
        return endpoints;
    }
}

public static class BackOfficeRoles
{
    public const string Viewer = "ledger-viewer";
    public const string Operator = "ledger-operator";
}

public static class BackOfficePolicies
{
    public const string Viewer = "viewer";
    public const string Operator = "operator";
}

public sealed class OidcSettings
{
    public string Authority { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string? ClientSecret { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;

    public void Validate()
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"{BackOfficeAuthentication.SectionName}:Authority must be an absolute URL, got '{Authority}'.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException($"{BackOfficeAuthentication.SectionName}:ClientId is missing.");
        }
    }
}
