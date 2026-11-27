using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace LedgerCore.Admin.Tests;

public sealed class SignInTests : IDisposable
{
    private const string Authority = "https://login.test/realms/ledger";

    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(host =>
        {
            host.UseSetting("Oidc:Authority", Authority);
            host.UseSetting("Oidc:ClientId", "ledger-admin");
            host.ConfigureTestServices(services =>
                // the real provider isn't there, so hand the handler its metadata instead of fetching it
                services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
                    {
                        Issuer = Authority,
                        AuthorizationEndpoint = $"{Authority}/protocol/openid-connect/auth",
                        EndSessionEndpoint = $"{Authority}/protocol/openid-connect/logout",
                    })));
        });

    [Fact]
    public async Task A_page_sends_an_anonymous_visitor_to_the_identity_provider()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri("/accounts", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith($"{Authority}/protocol/openid-connect/auth?", location, StringComparison.Ordinal);
        Assert.Contains("client_id=ledger-admin", location, StringComparison.Ordinal);
        Assert.Contains("response_type=code", location, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_probes_need_no_sign_in()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_viewer_sees_the_pages_with_their_name_and_a_sign_out_button()
    {
        using var client = SignedInAs("ana", BackOfficeRoles.Viewer);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<h1>Overview</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<span>ana</span>", html, StringComparison.Ordinal);
        Assert.Contains("Sign out", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_operator_can_read_the_pages_too()
    {
        using var client = SignedInAs("rui", BackOfficeRoles.Operator);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_without_a_ledger_role_is_refused()
    {
        using var client = SignedInAs("guest");

        var response = await client.GetAsync(new Uri("/accounts", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_without_the_antiforgery_token_is_rejected()
    {
        using var client = SignedInAs("ana", BackOfficeRoles.Viewer);

        var response = await client.PostAsync(
            new Uri("/sign-out", UriKind.Relative), new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", "ledger-admin", "Authority")]
    [InlineData("not a url", "ledger-admin", "Authority")]
    [InlineData(Authority, " ", "ClientId")]
    public void Settings_without_an_authority_or_client_fail_at_startup(string authority, string clientId, string named)
    {
        var settings = new OidcSettings { Authority = authority, ClientId = clientId };

        var error = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient SignedInAs(string name, params string[] roles) =>
        _factory.WithWebHostBuilder(host => host.ConfigureTestServices(services =>
            {
                services.AddSingleton(new TestUser(name, roles));
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = TestUserHandler.Name;
                        options.DefaultChallengeScheme = TestUserHandler.Name;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(TestUserHandler.Name, _ => { });
            }))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private sealed record TestUser(string Name, string[] Roles);

    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestUser user)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Claim[] claims =
            [
                new("preferred_username", user.Name),
                .. user.Roles.Select(role => new Claim(BackOfficeAuthentication.RolesClaim, role)),
            ];
            var identity = new ClaimsIdentity(claims, Name, "preferred_username", BackOfficeAuthentication.RolesClaim);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
