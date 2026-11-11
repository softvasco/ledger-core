using LedgerCore.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddLedger();
builder.Build().Run();
