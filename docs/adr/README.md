# Architecture decision records

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-event-sourcing-for-the-ledger.md) | Event sourcing for the ledger | Accepted |
| [0003](0003-results-for-business-rule-failures.md) | Results for business rule failures, exceptions for bugs | Accepted |
| [0004](0004-double-entry-bookkeeping-model.md) | Double-entry bookkeeping model | Accepted |
| [0005](0005-own-event-store-on-postgresql.md) | Own event store on PostgreSQL instead of Marten or KurrentDB | Accepted |
| [0006](0006-balance-on-the-account-stream.md) | The account stream keeps the balance for the funds check | Accepted |
| [0007](0007-own-command-and-query-dispatcher.md) | Own command and query dispatcher instead of MediatR | Accepted |
| [0008](0008-problem-details-for-every-error.md) | Problem details for every error response | Accepted |
| [0009](0009-idempotency-keys-for-post.md) | Idempotency keys for POST requests | Accepted |
| [0010](0010-idempotency-as-an-endpoint-filter.md) | Idempotency as an endpoint filter, so in-process callers get it | Accepted |
| [0011](0011-transfers-as-a-process-manager.md) | Transfers run as a process manager over two account streams | Accepted |
| [0012](0012-catch-up-projections-with-checkpoints.md) | Read models catch up from the global order with a checkpoint each | Accepted |
| [0013](0013-ef-core-for-the-sql-server-read-model.md) | EF Core for the SQL Server read model, hand-written SQL for the event store | Accepted |
| [0014](0014-postgresql-and-sql-server-behind-the-same-ports.md) | PostgreSQL and SQL Server behind the same ports | Accepted |
