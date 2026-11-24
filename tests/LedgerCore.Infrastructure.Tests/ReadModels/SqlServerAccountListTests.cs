using LedgerCore.Application.Accounts;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.ReadModels;
using LedgerCore.Infrastructure.Tests.Accounts;
using LedgerCore.Infrastructure.Tests.EventStore;

namespace LedgerCore.Infrastructure.Tests.ReadModels;

public class SqlServerAccountListTests(SqlServerFixture fixture)
    : AccountListContract, IClassFixture<SqlServerFixture>
{
    protected override async Task<(IRebuildableProjection Projection, IAccountList Accounts)> CreateAsync()
    {
        var readModel = new SqlServerAccountReadModel(await fixture.CreateDatabaseAsync(Token));
        await readModel.CreateSchemaAsync(Token);
        return (readModel, readModel);
    }
}
