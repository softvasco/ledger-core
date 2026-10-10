using LedgerCore.Api.Accounts;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.Commands;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Queries;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.Accounts;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.Snapshots;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// in memory until the PostgreSQL stores are wired to configuration
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton<ISnapshotStore<AccountSnapshot>, InMemorySnapshotStore<AccountSnapshot>>();
builder.Services.AddSingleton(new SnapshotPolicy(100));
builder.Services.AddSingleton<InMemoryAccountSummaries>();
builder.Services.AddSingleton<IAccountSummaries>(sp => sp.GetRequiredService<InMemoryAccountSummaries>());
builder.Services.AddHostedService<AccountSummariesCatchUp>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddCommands(typeof(OpenAccount).Assembly);
builder.Services.AddQueries(typeof(GetAccount).Assembly);

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapAccounts();

app.Run();
