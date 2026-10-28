using LedgerCore.Application.Accounts;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.Accounts;

namespace LedgerCore.Infrastructure.Tests.Accounts;

public class InMemoryAccountStatementsTests : AccountStatementsContract
{
    protected override Task<(IProjection Projection, IAccountStatements Statements)> CreateAsync()
    {
        var summaries = new InMemoryAccountSummaries();
        return Task.FromResult<(IProjection, IAccountStatements)>((summaries, summaries));
    }
}
