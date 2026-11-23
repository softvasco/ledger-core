using LedgerCore.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var api = builder.AddLedger();
builder.AddBackOffice(api);
builder.Build().Run();
