using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.ReadModels;

/// <summary>Balances and statement lines in SQL Server, with the checkpoint beside them.</summary>
public sealed class SqlServerAccountReadModel :
    ICheckpointedProjection, IRebuildableProjection, IAccountSummaries, IAccountStatements
{
    private const string DepositKind = "deposit";
    private const string WithdrawalKind = "withdrawal";

    private static readonly Func<ReadModelContext, Guid, CancellationToken, Task<AccountBalanceRow?>> BalanceQuery =
        EF.CompileAsyncQuery((ReadModelContext db, Guid accountId, CancellationToken _) =>
            db.Balances.AsNoTracking().SingleOrDefault(b => b.AccountId == accountId));

    private static readonly Func<ReadModelContext, Guid, CancellationToken, Task<string?>> CurrencyQuery =
        EF.CompileAsyncQuery((ReadModelContext db, Guid accountId, CancellationToken _) =>
            db.Balances.AsNoTracking()
                .Where(b => b.AccountId == accountId)
                .Select(b => b.Currency)
                .SingleOrDefault());

    // keyset paging on the stream version: a deposit landing mid-read can't shift the next page
    private static readonly Func<
        ReadModelContext, Guid, long, DateTimeOffset?, DateTimeOffset?, int, IAsyncEnumerable<StatementLineRow>> LinesQuery =
        EF.CompileAsyncQuery((
            ReadModelContext db, Guid accountId, long afterVersion, DateTimeOffset? from, DateTimeOffset? to, int take) =>
            db.StatementLines.AsNoTracking()
                .Where(l => l.AccountId == accountId && l.Version > afterVersion)
                .Where(l => from == null || l.OccurredAt >= from)
                .Where(l => to == null || l.OccurredAt < to)
                .OrderBy(l => l.Version)
                .Take(take));

    private readonly DbContextOptions<ReadModelContext> _options;

    public SqlServerAccountReadModel(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<ReadModelContext>().UseSqlServer(connectionString).Options;
    }

    public string Name => "sqlserver-account-balances";

    public async Task CreateSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = typeof(SqlServerAccountReadModel).Assembly.GetManifestResourceStream(
            "LedgerCore.Infrastructure.ReadModels.schema.sql")!;
        using var reader = new StreamReader(stream);
        var schema = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await using var db = new ReadModelContext(_options);
        await db.Database.ExecuteSqlRawAsync(schema, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetCheckpointAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ReadModelContext(_options);
        return await db.Checkpoints.AsNoTracking()
            .Where(c => c.Projection == Name)
            .Select(c => c.Position)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ReadModelContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.StatementLines.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Balances.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Checkpoints.Where(c => c.Projection == Name).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        await using var db = new ReadModelContext(_options);
        var checkpoint = await db.Checkpoints.FindAsync([Name], cancellationToken).ConfigureAwait(false)
            ?? db.Checkpoints.Add(new CheckpointRow { Projection = Name }).Entity;

        // a replay after a crash hands back events this checkpoint already covers
        if (recorded.Position <= checkpoint.Position)
        {
            return;
        }

        checkpoint.Position = recorded.Position;
        if (AccountSummaryProjection.TryGetAccount(recorded.Event, out var id))
        {
            await ApplyToAccountAsync(db, id, recorded, cancellationToken).ConfigureAwait(false);
        }

        // one SaveChanges is one transaction, so the rows and the checkpoint move together
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountSummary?> GetAsync(AccountId id, CancellationToken cancellationToken = default)
    {
        await using var db = new ReadModelContext(_options);
        var row = await BalanceQuery(db, id.Value, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToSummary(row);
    }

    public async Task<StatementPage?> GetAsync(GetStatement query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = new ReadModelContext(_options);
        var currencyCode = await CurrencyQuery(db, query.AccountId.Value, cancellationToken).ConfigureAwait(false);
        if (currencyCode is null)
        {
            return null;
        }

        var take = query.PageSize + 1;
        var rows = new List<StatementLineRow>(take);
        var lines = LinesQuery(db, query.AccountId.Value, query.AfterVersion, query.From, query.To, take);
        await foreach (var row in lines.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        var currency = Currency.FromCode(currencyCode);
        return StatementPaging.Page([.. rows.Select(r => ToLine(r, currency))], query.PageSize);
    }

    private static async Task ApplyToAccountAsync(
        ReadModelContext db,
        AccountId id,
        RecordedEvent recorded,
        CancellationToken cancellationToken)
    {
        var row = await db.Balances.FindAsync([id.Value], cancellationToken).ConfigureAwait(false);
        var summary = AccountSummaryProjection.Apply(row is null ? null : ToSummary(row), recorded);
        row ??= db.Balances.Add(new AccountBalanceRow { AccountId = id.Value }).Entity;

        row.Iban = summary.Iban.Value;
        row.Currency = summary.Currency.Code;
        row.Status = summary.Status.ToString();
        row.Balance = summary.Balance.Amount;
        row.Version = summary.Version;

        var line = recorded.Event switch
        {
            MoneyDeposited e => (Kind: DepositKind, Amount: e.Amount.Amount, e.OccurredAt),
            MoneyWithdrawn e => (Kind: WithdrawalKind, Amount: -e.Amount.Amount, e.OccurredAt),
            _ => default,
        };
        if (line.Kind is not null)
        {
            db.StatementLines.Add(new StatementLineRow
            {
                AccountId = id.Value,
                Version = recorded.Version,
                Position = recorded.Position,
                Kind = line.Kind,
                Amount = line.Amount,
                BalanceAfter = summary.Balance.Amount,
                OccurredAt = line.OccurredAt,
            });
        }
    }

    private static StatementLine ToLine(StatementLineRow row, Currency currency) => new(
        row.Version,
        row.Kind == DepositKind ? StatementLineKind.Deposit : StatementLineKind.Withdrawal,
        Money.Of(row.Amount, currency),
        Money.Of(row.BalanceAfter, currency),
        row.OccurredAt);

    private static AccountSummary ToSummary(AccountBalanceRow row)
    {
        var currency = Currency.FromCode(row.Currency);
        return new AccountSummary(
            AccountId.From(row.AccountId),
            Iban.Parse(row.Iban),
            currency,
            Enum.Parse<AccountStatus>(row.Status),
            Money.Of(row.Balance, currency),
            row.Version);
    }
}
