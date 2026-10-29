using LedgerCore.Api.Accounts;
using LedgerCore.Api.OpenApi;
using LedgerCore.Api.Projections;
using LedgerCore.Api.Problems;
using LedgerCore.Api.Storage;
using LedgerCore.Api.Transfers;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.Commands;
using LedgerCore.Application.Idempotency;
using LedgerCore.Application.Projections;
using LedgerCore.Application.Queries;
using LedgerCore.Application.Snapshots;
using LedgerCore.Application.Transfers;
using LedgerCore.Infrastructure.Projections;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddLedgerOpenApi();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddLedgerStorage(builder.Configuration);
builder.Services.AddSingleton(new IdempotencyOptions());
builder.Services.AddSingleton(new SnapshotPolicy(100));

// for read models in memory; the SQL Server one keeps its checkpoint in its own tables
builder.Services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();
builder.Services.AddSingleton<ProjectionRunner>();
builder.Services.AddHostedService<ProjectionCatchUp>();
builder.Services.AddSingleton<ProjectionRebuilds>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<TransferRepository>();
builder.Services.AddScoped<TransferProcessManager>();
builder.Services.AddCommands(typeof(OpenAccount).Assembly);
builder.Services.AddQueries(typeof(GetAccount).Assembly);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapAccounts();
app.MapTransfers();
app.MapProjectionAdmin(app.Configuration);

app.Run();
