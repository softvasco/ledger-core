using LedgerCore.Infrastructure.Snapshots;
using LedgerCore.Infrastructure.Tests.EventStore;

namespace LedgerCore.Infrastructure.Tests.Snapshots;

public class PostgresSnapshotStoreTests(PostgresFixture fixture)
    : SnapshotStoreContract(PostgresSnapshotStore.ForAccounts(fixture.DataSource)), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_snapshot_that_no_longer_reads_is_treated_as_missing()
    {
        var stream = NewStream();
        await using var command = fixture.DataSource.CreateCommand(
            "insert into snapshots (stream_category, stream_id, version, data) values (@category, @id, 3, @data::jsonb)");
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        command.Parameters.AddWithValue(
            "data",
            $$"""{"accountId":"{{stream.Id}}","iban":"PT50000201231234567890154","currency":"EUR","status":"Dormant","version":3}""");
        await command.ExecuteNonQueryAsync(Token);

        Assert.Null(await PostgresSnapshotStore.ForAccounts(fixture.DataSource).LoadAsync(stream, Token));
    }

    // without the required check the old row would load with a null balance instead of being skipped
    [Fact]
    public async Task A_snapshot_from_before_balances_is_treated_as_missing()
    {
        var stream = NewStream();
        await using var command = fixture.DataSource.CreateCommand(
            "insert into snapshots (stream_category, stream_id, version, data) values (@category, @id, 3, @data::jsonb)");
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        command.Parameters.AddWithValue(
            "data",
            $$"""{"accountId":"{{stream.Id}}","iban":"PT50000201231234567890154","currency":"EUR","status":"Open","version":3}""");
        await command.ExecuteNonQueryAsync(Token);

        Assert.Null(await PostgresSnapshotStore.ForAccounts(fixture.DataSource).LoadAsync(stream, Token));
    }
}
