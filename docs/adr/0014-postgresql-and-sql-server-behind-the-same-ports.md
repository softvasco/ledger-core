# 14. PostgreSQL and SQL Server behind the same ports

Date: 2026-10-28

## Status

Accepted

## Context

The event store started on PostgreSQL (ADR 0005). A lot of .NET teams run SQL Server, and it is the database I have used most, so a ledger that only ran on PostgreSQL would leave out the setup many readers have. Two engines also cost something: every storage feature is written twice, and the differences have to be tested, not assumed.

## Decision

Both engines implement the same ports: `IEventStore`, `ISnapshotStore<T>` and `IIdempotencyStore`. Each port has one contract test class, and the PostgreSQL and SQL Server tests inherit from it and run against a real database through Testcontainers. `Storage:Provider` (`PostgreSql` or `SqlServer`) picks the engine when `ConnectionStrings:Ledger` is set; without a connection string everything stays in memory.

Each engine gets its own SQL rather than a lowest common denominator. These are the places where they differ:

| | PostgreSQL | SQL Server |
|---|---|---|
| Global position | `generated always as identity` | `identity(1, 1)` |
| When the position is assigned | at insert, before commit | at insert, before commit |
| Append lock | `pg_advisory_xact_lock` inside a select | `sp_getapplock` owned by the transaction |
| Several events in one append | an `NpgsqlBatch` of inserts | one `insert ... select ... order by`, 500 rows per statement |
| Upsert (snapshots, idempotency keys) | `insert ... on conflict do update` | `update with (updlock, serializable)`, then insert where not exists |
| Event JSON | `jsonb` | `nvarchar(max)` with an `isjson` check |
| Reader during an open append | sees the last committed row (MVCC) | blocks on the locked rows, unless READ_COMMITTED_SNAPSHOT is on |
| Read model | in memory, rebuilt on start | tables through EF Core (ADR 0013) |

The two rows that matter most:

- Identity timing. On both engines a position is handed out at insert and becomes visible at commit, so position 8 can commit before position 7. A reader following the global order would skip 7 for good. Both stores take one append lock per database, which makes appends commit in position order. It costs throughput (`docs/performance.md` has the numbers) and keeps readers simple.
- No MERGE on SQL Server. Two MERGE statements can both decide a new key is missing and both insert, so the second fails on the primary key. The `updlock, serializable` hints take a range lock on the missing key, and the second writer waits instead. A test races 600 first saves to check it.

The SQL Server tests run with READ_COMMITTED_SNAPSHOT on, because then readers behave like PostgreSQL and don't wait, which is the harder case for the append order. Without it, readers block until the append commits, which is slower but can't skip anything.

## Consequences

- A storage change is two implementations and one contract. It takes longer, and a feature that one engine can't do well has to be redesigned for both or not done.
- The contract tests are what keeps the two equal. A difference that no test covers will drift.
- Amounts come back with the scale of the column on SQL Server (`decimal(19, 4)`, so `100.0000`), while the in-memory read model returns `100`. Same value, different JSON. Not normalised yet.
- Picking an engine is configuration, not a build flag, so one image runs on either.
