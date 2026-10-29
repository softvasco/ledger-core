using LedgerCore.Application.Accounts;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.Accounts;

namespace LedgerCore.Infrastructure.Tests.Accounts;

public class InMemoryAccountStatementsTests : AccountStatementsContract
{
    protected override Task<(IRebuildableProjection Projection, IAccountStatements Statements)> CreateAsync()
    {
        var summaries = new InMemoryAccountSummaries();
        return Task.FromResult<(IRebuildableProjection, IAccountStatements)>((summaries, summaries));
    }
}
