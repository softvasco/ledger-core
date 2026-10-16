using LedgerCore.Application.Idempotency;
using LedgerCore.Infrastructure.Idempotency;
using LedgerCore.Infrastructure.Tests.EventStore;

namespace LedgerCore.Infrastructure.Tests.Idempotency;

public class PostgresIdempotencyStoreTests(PostgresFixture fixture)
    : IdempotencyStoreContract(time => new PostgresIdempotencyStore(fixture.DataSource, Options, time)), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Only_one_of_many_concurrent_claims_wins()
    {
        var key = Guid.CreateVersion7().ToString();

        var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Store.ClaimAsync(key, [1, 2, 3], Token)));

        Assert.Single(claims, c => c == IdempotencyClaim.Claimed);
        Assert.All(claims.Where(c => c != IdempotencyClaim.Claimed), c => Assert.Equal(IdempotencyClaim.InProgress, c));
    }
}
