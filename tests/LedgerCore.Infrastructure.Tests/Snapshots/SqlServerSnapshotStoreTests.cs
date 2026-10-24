using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.Snapshots;
using LedgerCore.Infrastructure.Tests.EventStore;
using Microsoft.Data.SqlClient;

namespace LedgerCore.Infrastructure.Tests.Snapshots;

public class SqlServerSnapshotStoreTests(SqlServerFixture fixture)
    : SnapshotStoreContract(SqlServerSnapshotStore.ForAccounts(fixture.ConnectionString)), IClassFixture<SqlServerFixture>
{
    // without the range locks this fails with a primary key violation: two saves both see no row
    [Fact]
    public async Task Racing_first_saves_never_fail_and_the_latest_wins()
    {
        var store = SqlServerSnapshotStore.ForAccounts(fixture.ConnectionString);
        var streams = Enumerable.Range(0, 20).Select(_ => NewStream()).ToArray();
        var saves = streams
            .SelectMany(stream => Enumerable.Range(1, 30).Select(version => (stream, version)))
            .ToArray();

        await Task.WhenAll(saves.Select(s =>
            store.SaveAsync(s.stream, s.version, Taken(s.stream, s.version, AccountStatus.Open), Token)));

        foreach (var stream in streams)
        {
            Assert.Equal(30, (await store.LoadAsync(stream, Token))!.Version);
        }
    }

    [Fact]
    public async Task A_snapshot_that_no_longer_reads_is_treated_as_missing()
    {
        var stream = NewStream();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new SqlCommand(
            "insert into dbo.snapshots (stream_category, stream_id, version, data) values (@category, @id, 3, @data)",
            connection);
        command.Parameters.AddWithValue("@category", stream.Category);
        command.Parameters.AddWithValue("@id", stream.Id);
        command.Parameters.AddWithValue(
            "@data",
            $$"""{"accountId":"{{stream.Id}}","iban":"PT50000201231234567890154","currency":"EUR","status":"Dormant","version":3}""");
        await command.ExecuteNonQueryAsync(Token);

        Assert.Null(await SqlServerSnapshotStore.ForAccounts(fixture.ConnectionString).LoadAsync(stream, Token));
    }
}
