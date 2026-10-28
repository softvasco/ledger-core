using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.Projections;
using LedgerCore.Infrastructure.ReadModels;
using LedgerCore.Infrastructure.Tests.EventStore;
using Microsoft.Data.SqlClient;

namespace LedgerCore.Infrastructure.Tests.ReadModels;

public class SqlServerAccountReadModelTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly Iban Iban = Iban.Parse("PT50000201231234567890154");
    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly AccountId _account = AccountId.From(Guid.CreateVersion7());
    private string _connectionString = "";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deposits_and_withdrawals_give_the_balance_and_one_statement_line_each()
    {
        var readModel = await NewReadModel();

        await Apply(readModel, Opened(), Deposited(100.50m), Withdrawn(30m));

        var summary = await readModel.GetAsync(_account, Token);
        Assert.NotNull(summary);
        Assert.Equal(Money.Of(70.50m, Currency.Eur), summary.Balance);
        Assert.Equal(3, summary.Version);
        Assert.Equal(
            [("deposit", 100.50m, 100.50m), ("withdrawal", -30m, 70.50m)],
            await StatementLines());
        Assert.Equal(3, await readModel.GetCheckpointAsync(Token));
    }

    [Fact]
    public async Task Events_the_checkpoint_already_covers_change_nothing()
    {
        var readModel = await NewReadModel();
        await Apply(readModel, Opened(), Deposited(100m));

        // what a crash between commit and the next read looks like: the same events again
        await Apply(readModel, Opened(), Deposited(100m));

        Assert.Equal(Money.Of(100m, Currency.Eur), (await readModel.GetAsync(_account, Token))!.Balance);
        Assert.Single(await StatementLines());
    }

    [Fact]
    public async Task Other_events_only_move_the_checkpoint()
    {
        var readModel = await NewReadModel();

        await Apply(readModel, new SomethingHappened(1, Monday));

        Assert.Equal(1, await readModel.GetCheckpointAsync(Token));
        Assert.Null(await readModel.GetAsync(_account, Token));
    }

    [Fact]
    public async Task A_restarted_runner_picks_up_from_the_checkpoint_in_the_database()
    {
        var connectionString = await NewDatabase();
        var events = new InMemoryEventStore();
        var stream = StreamId.For("account", _account.Value);
        await events.AppendAsync(stream, 0, [Opened(), Deposited(10m)], Token);

        var first = new SqlServerAccountReadModel(connectionString);
        await first.CreateSchemaAsync(Token);
        using (var runner = new ProjectionRunner(events, new InMemoryCheckpointStore(), [first]))
        {
            Assert.Equal(2, await runner.CatchUpAsync(Token));
        }

        await events.AppendAsync(stream, 2, [Deposited(5m)], Token);

        // a fresh in-memory checkpoint store, as after a restart: the database still knows where it was
        var restarted = new SqlServerAccountReadModel(connectionString);
        using var again = new ProjectionRunner(events, new InMemoryCheckpointStore(), [restarted]);
        Assert.Equal(1, await again.CatchUpAsync(Token));
        Assert.Equal(Money.Of(15m, Currency.Eur), (await restarted.GetAsync(_account, Token))!.Balance);
    }

    [Fact]
    public async Task Creating_the_schema_twice_is_harmless()
    {
        var readModel = await NewReadModel();

        await readModel.CreateSchemaAsync(Token);
    }

    private static async Task Apply(SqlServerAccountReadModel readModel, params IDomainEvent[] events)
    {
        var stream = StreamId.For("account", Guid.CreateVersion7());
        for (var i = 0; i < events.Length; i++)
        {
            var account = AccountOf(events[i]);
            var recorded = new RecordedEvent(
                account is { } id ? StreamId.For("account", id.Value) : stream, i + 1, i + 1, events[i]);
            await readModel.ApplyAsync(recorded, Token);
        }
    }

    private static AccountId? AccountOf(IDomainEvent domainEvent) =>
        AccountSummaryProjection.TryGetAccount(domainEvent, out var id) ? id : null;

    private async Task<IReadOnlyList<(string Kind, decimal Amount, decimal BalanceAfter)>> StatementLines()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(Token);
        await using var command = new SqlCommand(
            "select kind, amount, balance_after from dbo.statement_lines where account_id = @id order by version",
            connection);
        command.Parameters.AddWithValue("@id", _account.Value);
        await using var reader = await command.ExecuteReaderAsync(Token);
        var lines = new List<(string, decimal, decimal)>();
        while (await reader.ReadAsync(Token))
        {
            lines.Add((reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2)));
        }

        return lines;
    }

    private async Task<SqlServerAccountReadModel> NewReadModel()
    {
        _connectionString = await NewDatabase();
        var readModel = new SqlServerAccountReadModel(_connectionString);
        await readModel.CreateSchemaAsync(Token);
        return readModel;
    }

    // every test gets its own database, since they all share the projection's one checkpoint row
    private async Task<string> NewDatabase()
    {
        var name = $"read_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        await using var create = new SqlCommand($"create database {name}", connection);
        await create.ExecuteNonQueryAsync(Token);
        return new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name }.ConnectionString;
    }

    private AccountOpened Opened() => new(_account, Iban, Currency.Eur, Monday);

    private MoneyDeposited Deposited(decimal amount) => new(_account, Money.Of(amount, Currency.Eur), Monday);

    private MoneyWithdrawn Withdrawn(decimal amount) => new(_account, Money.Of(amount, Currency.Eur), Monday);
}
