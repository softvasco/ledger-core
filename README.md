# LedgerCore

[![ci](https://github.com/softvasco/ledger-core/actions/workflows/ci.yml/badge.svg)](https://github.com/softvasco/ledger-core/actions/workflows/ci.yml)
[![license](https://img.shields.io/github/license/softvasco/ledger-core)](LICENSE)

An event-sourced, double-entry banking ledger in .NET 10. Every movement debits one account and credits another, nothing is ever updated in place, and any balance can be rebuilt from the events that produced it.

Work in progress. The domain model, the event store (PostgreSQL or SQL Server), the command and query layer and the HTTP API are in place; the transactional outbox is next.

## Why

In a lot of banking software the balance is a column that gets overwritten, and the audit trail is a separate table you hope is complete. When a number looks wrong, working out how it got there is slow and painful.

A ledger built on events turns that around. The events are the record, the balance is derived from them, and the history is the same thing as the audit. Double entry adds the rule that money never appears or disappears: for every posting, debits equal credits.

This repo is my take on how that should look in modern .NET, without a framework hiding the interesting parts.

## Features

Done:
- `Money` and `Currency` value objects: no mixing currencies, no amounts finer than the currency settles in, banker's rounding on multiplication.
- `Iban` with per-country length and mod-97 check, and a strongly typed `AccountId` (time-ordered v7 Guid).
- `Account` aggregate (open, freeze, unfreeze, close) built from its events on a small aggregate root with versioning.
- Journal entries of two or more postings that must balance in every currency, checked with FsCheck property tests ([ADR-0004](docs/adr/0004-double-entry-bookkeeping-model.md)).
- Business rule failures come back as `Result` with stable error codes; exceptions are kept for bugs ([ADR-0003](docs/adr/0003-results-for-business-rule-failures.md)).
- Event store on a single table: optimistic concurrency per stream, and a global position that readers can follow without skipping a late commit. It runs on PostgreSQL or SQL Server and both pass the same contract tests. Positions come from an identity column that is assigned before commit, so appends take a lock first: an advisory lock on PostgreSQL, `sp_getapplock` on SQL Server.
- Snapshots every N events (100 by default). They are only a cache: a missing or unreadable snapshot just means a longer replay.
- Benchmarks for appends, stream reads and the SQL Server read model, with the results in [docs/performance.md](docs/performance.md). One append takes about 2.4 ms on PostgreSQL and 4.3 ms on SQL Server; a 50-line statement page about 1.4 ms with compiled EF Core queries.
- Commands and queries through a small hand-written dispatcher, one handler per message ([ADR-0007](docs/adr/0007-own-command-and-query-dispatcher.md)). Commands run through tracing, logging and validation behaviors; validation reports every error at once.
- Deposit and Withdraw with the balance kept on the account stream, so two racing withdrawals can't both pass the funds check ([ADR-0006](docs/adr/0006-balance-on-the-account-stream.md)).
- Read models fed by a projection runner that follows the store's global position, with a checkpoint per projection ([ADR-0012](docs/adr/0012-catch-up-projections-with-checkpoints.md)). On SQL Server, balances and statement lines are tables written with EF Core, each event committing its rows and the checkpoint together ([ADR-0013](docs/adr/0013-ef-core-for-the-sql-server-read-model.md)).
- Rebuilding a read model without a restart: `POST /admin/projections/{name}/rebuild` empties it and replays the store in the background, and a GET on the same route shows how far it has got. Off unless `Admin:Enabled` is true ([ADR-0015](docs/adr/0015-rebuilding-a-projection-in-place.md)).
- `GET /accounts/{id}/statement`: deposits and withdrawals, oldest first, with the balance after each. The cursor is the stream version, so a deposit that lands between two pages doesn't shift the next one.
- Account endpoints on a minimal API. Every error is an RFC 9457 problem with a stable `code` to branch on, for example a 409 with `account.insufficient_funds` when a withdrawal is more than the balance ([ADR-0008](docs/adr/0008-problem-details-for-every-error.md)).
- An OpenAPI 3.1 document at `/openapi/v1.json` that lists every status an endpoint can answer, and a Scalar UI at `/scalar` in development.
- `Idempotency-Key` on every POST: a retry gets the stored response back, the same key with another body is a 422 ([ADR-0009](docs/adr/0009-idempotency-keys-for-post.md)). It is an endpoint filter, so callers that run the endpoint in-process get it too ([ADR-0010](docs/adr/0010-idempotency-as-an-endpoint-filter.md)).
- Transfers between accounts run as a process manager over the two account streams: debit the source, credit the destination, and give the money back if the destination refuses ([ADR-0011](docs/adr/0011-transfers-as-a-process-manager.md)). `POST /transfers` answers 201 with the final status, or the refusing account's problem plus a `transferId` to read it back from `GET /transfers/{id}`. A retry with the same `Idempotency-Key` gets the first answer and moves the money once.
- What counts as the API contract, which changes break it, and how a v2 would sit next to v1: [docs/api.md](docs/api.md).

Coming next:
- Transactional outbox (built on outbox-kit), then Azure Service Bus.
- .NET Aspire, OpenTelemetry, and a Blazor back office.

## Architecture

```mermaid
flowchart LR
    client[API client] --> api[LedgerCore.Api<br/>minimal APIs]
    api --> app[LedgerCore.Application<br/>commands and queries]
    app --> domain[LedgerCore.Domain<br/>aggregates, value objects, events]
    app --> ports{{ports}}
    infra[LedgerCore.Infrastructure] -. implements .-> ports
    infra --> es[(PostgreSQL or SQL Server<br/>event store + outbox)]
    infra --> rm[(SQL Server<br/>read models)]
    infra --> bus[[Azure Service Bus]]
```

The Domain project has no dependencies at all. Application talks to the outside world only through interfaces it owns, and Infrastructure implements them. Design decisions are in [docs/adr](docs/adr/README.md), starting with [why the ledger is event sourced](docs/adr/0002-event-sourcing-for-the-ledger.md).

## How a command and a query flow

```mermaid
sequenceDiagram
    participant C as Caller
    participant D as CommandDispatcher
    participant B as Behaviors<br/>tracing, logging, validation
    participant H as WithdrawHandler
    participant R as AccountRepository
    participant S as Event store
    participant P as Projection
    participant Q as QueryDispatcher

    C->>D: Withdraw(accountId, 40.50 EUR)
    D->>B: run behaviors in order
    B->>H: valid command
    H->>R: load account
    R->>S: snapshot + later events
    H->>H: Account.Withdraw (funds check)
    H->>R: save
    R->>S: append at expected version
    H-->>C: Result with the new balance
    S-->>P: events in global order
    P->>P: fold into AccountSummary
    C->>Q: GetAccount(accountId)
    Q-->>C: summary as of the projection's checkpoint
```

The write side answers from the account stream, so a withdrawal never sees a stale balance. The read side catches up after the append and can lag behind it; a query returns whatever the projection has seen so far.

## How a transfer runs

```mermaid
stateDiagram-v2
    [*] --> Requested: POST /transfers
    Requested --> SourceDebited: source withdrawal accepted
    Requested --> Failed: source refuses
    SourceDebited --> Completed: destination deposit accepted
    SourceDebited --> Compensated: destination refuses, money back on the source
    SourceDebited --> SourceDebited: refund refused too, stays open for a person
    Completed --> [*]
    Failed --> [*]
    Compensated --> [*]
```

Each arrow is one event on the transfer's own stream, saved before the next step starts, so after a crash `TransferProcessManager` picks up at the last recorded state. A refused debit is `Failed` because no money moved. A refused credit is `Compensated` because the source was paid back. If the refund is refused as well, the transfer stays `SourceDebited` and the error is thrown rather than hidden ([ADR-0011](docs/adr/0011-transfers-as-a-process-manager.md)).

## Quickstart

```bash
dotnet build
dotnet test
dotnet run --project src/LedgerCore.Api
```

Without a connection string the API keeps everything in memory. Open http://localhost:5174/scalar to open an account, post deposits and withdrawals and move money between accounts, or run the requests in [LedgerCore.Api.http](src/LedgerCore.Api/LedgerCore.Api.http) from VS Code (REST Client) or Visual Studio. They cover each endpoint, a retried deposit, and the main errors.

### On SQL Server

```bash
docker run -d --name ledger-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Ledger_dev_1' -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
docker exec ledger-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Ledger_dev_1' -Q "create database ledger"

export ConnectionStrings__Ledger='Server=localhost,14333;Database=ledger;User Id=sa;Password=Ledger_dev_1;TrustServerCertificate=true'
export Storage__Provider=SqlServer
dotnet run --project src/LedgerCore.Api
```

The API creates its tables on start. Balances and statements come from SQL Server tables that survive a restart (`GET /accounts/{id}/statement`).

### On PostgreSQL

```bash
docker run -d --name ledger-pg -e POSTGRES_PASSWORD=ledger -e POSTGRES_DB=ledger -p 15432:5432 postgres:18-alpine

export ConnectionStrings__Ledger='Host=localhost;Port=15432;Database=ledger;Username=postgres;Password=ledger'
export Storage__Provider=PostgreSql
dotnet run --project src/LedgerCore.Api
```

On PostgreSQL the read model is still in memory and replays the events on start. What differs between the two engines, and why, is in [ADR-0014](docs/adr/0014-postgresql-and-sql-server-behind-the-same-ports.md).

Needs the .NET 10 SDK, and Docker for the databases and for the PostgreSQL and SQL Server tests (Testcontainers starts them). A one-command run with .NET Aspire comes later.

## License

MIT
