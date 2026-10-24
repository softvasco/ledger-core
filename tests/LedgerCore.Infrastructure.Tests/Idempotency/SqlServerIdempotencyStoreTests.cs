using LedgerCore.Application.Idempotency;
using LedgerCore.Infrastructure.Idempotency;
using LedgerCore.Infrastructure.Tests.EventStore;

namespace LedgerCore.Infrastructure.Tests.Idempotency;

public class SqlServerIdempotencyStoreTests(SqlServerFixture fixture)
    : IdempotencyStoreContract(time => new SqlServerIdempotencyStore(fixture.ConnectionString, Options, time)),
        IClassFixture<SqlServerFixture>
{
    // many keys at once, since one key alone rarely hits the window where two claims both see no row
    [Fact]
    public async Task Only_one_concurrent_claim_per_key_wins()
    {
        var keys = Enumerable.Range(0, 20).Select(_ => Guid.CreateVersion7().ToString()).ToArray();

        var claims = await Task.WhenAll(keys
            .SelectMany(key => Enumerable.Range(0, 20).Select(_ => key))
            .Select(async key => (key, claim: await Store.ClaimAsync(key, [1, 2, 3], Token))));

        foreach (var perKey in claims.GroupBy(c => c.key))
        {
            Assert.Single(perKey, c => c.claim == IdempotencyClaim.Claimed);
            Assert.All(perKey.Where(c => c.claim != IdempotencyClaim.Claimed), c => Assert.Equal(IdempotencyClaim.InProgress, c.claim));
        }
    }
}
