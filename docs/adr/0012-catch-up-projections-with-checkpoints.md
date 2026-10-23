# 12. Read models catch up from the global order with a checkpoint each

Date: 2026-10-23

## Status

Accepted

## Context

The account read model kept its own position and its own loop over `ReadAllAsync`. The next read models (balances and statements on SQL Server) would each need the same loop, the same lock and the same retry, and a durable model also needs its position to survive a restart.

I looked at three ways to feed them:

- A subscription that pushes events after each append. Fast, but an event appended while the reader is down is gone unless there is also a catch-up path, so it ends up being both.
- Each read model polls on its own. Simple, but the checkpoint handling gets copied around and drifts.
- One runner that polls the store's global order and hands each projection the events after its own checkpoint.

## Decision

`IProjection` has a name and `ApplyAsync(RecordedEvent)`. `ICheckpointStore` keeps the last position per projection name. `ProjectionRunner` reads from each projection's checkpoint to the end of the store, applies the events in order and saves the checkpoint every 100 events and at the end of the pass. When a projection throws, the runner saves the position of the last event that went through, so the next pass starts at the one that failed. A background service runs a pass every 200 ms.

Delivery is at least once. A crash between applying events and saving the checkpoint replays them, so a projection must ignore an event it has already seen. `AccountSummaryProjection` already does this by comparing the stream version.

The checkpoint has to live where the projection keeps its data. The account summaries are in memory, so their checkpoint is in memory too. A checkpoint in PostgreSQL next to an in-memory model would make a restarted app skip every event before it. The SQL Server read models will save their rows and their checkpoint in the same transaction.

## Consequences

- One slow or broken projection holds up the ones after it in the same pass. Fine with one or two read models; if that changes, each projection gets its own loop.
- Positions can have gaps (a rolled-back append still uses up its sequence values), so the checkpoint is the last position seen, never a count.
- A projection renamed by mistake starts again from position 0. That rebuilds it rather than losing data, which is also how a rebuild will work.
- Polling every 200 ms costs a query per pass when nothing changed. Good enough until the outbox gives a signal to wake up on.
