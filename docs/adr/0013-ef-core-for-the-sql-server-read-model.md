# 13. EF Core for the SQL Server read model, hand-written SQL for the event store

Date: 2026-10-28

## Status

Accepted

## Context

Balances and statement lines need tables a reader can query with paging and filters, and they need to survive a restart. ADR 0012 says a durable read model keeps its checkpoint next to its rows. With the runner saving checkpoints through `ICheckpointStore`, the rows and the checkpoint would commit in two transactions, and a crash between them replays events into rows that already have them.

Everything else on SQL Server is ADO.NET with hand-written SQL. The append path in `SqlServerEventStore` needs `sp_getapplock`, a check on the stream version and one multi-row insert, and an ORM would only get in the way there.

## Decision

The read model uses EF Core (`Microsoft.EntityFrameworkCore.SqlServer`). The write side stays as it is.

- Three tables: `account_balances` (one row per account), `statement_lines` (one row per deposit or withdrawal, keyed by account and stream version, with the balance after it) and `projection_checkpoints`.
- `SqlServerAccountReadModel` implements `ICheckpointedProjection`. Each event is applied in one `SaveChanges`: the balance row, the statement line and the checkpoint row commit together or not at all. The runner reads the checkpoint from the projection and never saves one for it.
- An event at or below the stored checkpoint is skipped, so a replay changes nothing.
- The tables come from an idempotent `schema.sql`, like the event store's, and not from EF migrations. The read model can always be rebuilt from the events: when its shape changes, the new version gets a new projection name and starts again from position 0.
- The balance logic is `AccountSummaryProjection.Apply`, the same fold the in-memory model uses, so the two can't disagree.

EF Core earns its place on the query side, which is next: statements with paging and filters read better as LINQ than as SQL strings, and `AsNoTracking` and compiled queries are there when the performance pass comes.

## Consequences

- One transaction per event. A full rebuild of a large store will be slow; batching several events into one `SaveChanges` is a change inside the projection, measured in the performance pass.
- Only one runner may apply events at a time. The runner's lock covers one process; two API instances on the same database would both apply the same events, and the second insert of a statement line would fail on the primary key. Running more than one instance needs a database lock around the pass first.
- The PostgreSQL and in-memory setups still use the in-memory summaries, so on PostgreSQL every restart replays all events. A PostgreSQL version of this read model is the same code with a different provider.
- EF Core 10 depends on Microsoft.Data.SqlClient 6.1; central package pinning lifts it to 7.1.1, which the tests run against.
