using LedgerCore.Application.Accounts;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.Accounts;

namespace LedgerCore.Infrastructure.Tests.Accounts;

public class InMemoryAccountListTests : AccountListContract
{
    protected override Task<(IRebuildableProjection Projection, IAccountList Accounts)> CreateAsync()
    {
        var summaries = new InMemoryAccountSummaries();
        return Task.FromResult<(IRebuildableProjection, IAccountList)>((summaries, summaries));
    }
}
