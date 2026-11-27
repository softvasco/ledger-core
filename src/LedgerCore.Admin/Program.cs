using LedgerCore.Admin;
using LedgerCore.Admin.Components;
using LedgerCore.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddServiceDiscovery();

// "api" is the resource name in the AppHost; service discovery turns it into the API's address
builder.Services.AddHttpClient<LedgerApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddServiceDiscovery();

builder.Services.AddBackOfficeAuthentication(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapDefaultEndpoints();
app.MapSignOut();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
