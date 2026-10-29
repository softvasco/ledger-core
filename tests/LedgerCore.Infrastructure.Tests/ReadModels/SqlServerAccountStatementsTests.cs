using LedgerCore.Application.Accounts;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.ReadModels;
using LedgerCore.Infrastructure.Tests.Accounts;
using LedgerCore.Infrastructure.Tests.EventStore;

namespace LedgerCore.Infrastructure.Tests.ReadModels;

public class SqlServerAccountStatementsTests(SqlServerFixture fixture)
    : AccountStatementsContract, IClassFixture<SqlServerFixture>
{
    protected override async Task<(IRebuildableProjection Projection, IAccountStatements Statements)> CreateAsync()
    {
        var readModel = new SqlServerAccountReadModel(await fixture.CreateDatabaseAsync(Token));
        await readModel.CreateSchemaAsync(Token);
        return (readModel, readModel);
    }
}
