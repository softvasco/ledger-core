# 5. Own event store on PostgreSQL instead of Marten or KurrentDB

Date: 2026-10-06

## Status

Accepted

## Context

ADR-0002 made the ledger event sourced and left the choice of store open. The store is now written (one `events` table, one `snapshots` table, an advisory lock on append, Testcontainers tests), so this records why it is hand-written and what would make me swap it out.

The options were:

1. Marten (JasperFx, MIT). Document database and event store on PostgreSQL. Comes with projections, an async daemon for them, inline and live aggregation, tenancy, and its own schema management.
2. KurrentDB, formerly EventStoreDB. A database built for event streams, with subscriptions and a gRPC client for .NET. Since the 24.10 release it ships under its own source-available licence (ESLv2, later Kurrent License v1), which is not OSI approved. The core server is free to use; some features need a licence key.
3. A store written in this repository on PostgreSQL, with the surface area the ledger needs and nothing else.

## Decision

The ledger uses its own store on PostgreSQL.

The reasons, in order of weight:

- The point of this repository is to show the engineering of a ledger, and the store is a large part of it. Append ordering, optimistic concurrency, the global position a projection can follow, snapshots as a cache: with Marten those are decisions someone else made, and this repo would be a thin layer over a library. Written here, they are mine to explain and to get wrong in public.
- The surface is small. `IEventStore` has three operations and `ISnapshotStore` has two. The PostgreSQL implementation is a few hundred lines, two tables and one lock, and the whole of it is tested against a real PostgreSQL in CI.
- One database, already there. The ledger needs PostgreSQL for projections and the outbox anyway. KurrentDB would be a second server to run, back up and secure, for a project that is nowhere near the write rates where a purpose-built store pays off.
- Licensing. MIT for Marten is fine. KurrentDB's licence would be fine for this project too, but I'd rather not build a reference implementation on a store whose licence has already changed once.

What the store does not have, and Marten does: a projection runner with checkpoints and rebuilds, upcasting of old event versions, multi-tenancy, and years of production use by other people. The projection runner is on the roadmap, and I expect it to be the point where the gap to Marten is most visible.

## Consequences

- Every store feature the ledger needs gets written and tested here. That is the cost and the reason at the same time.
- The append lock serialises all appends across streams. Measured single-append latency is in `docs/performance.md`; concurrent throughput is not measured yet, and the lock is the first thing to revisit if it ever matters.
- The interfaces stay small and owned by the Application project, so a swap to Marten would be a new Infrastructure implementation and a migration of the events table, not a change to the domain or the handlers. If this were a product with a deadline rather than a reference ledger, Marten is what I would pick.
- Snapshots of accounts are a cache and can be thrown away at any time (see the snapshot store comments), so a change of store does not have to carry them over.
