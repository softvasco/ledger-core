# Performance

Numbers for the PostgreSQL and SQL Server event stores, from `benchmarks/LedgerCore.Benchmarks`. Both engines run the same three benchmarks from one base class. They are a baseline to compare later changes against, not a claim about production throughput.

## Results

| Benchmark | PostgreSQL | SQL Server | PostgreSQL allocated | SQL Server allocated |
|---|---:|---:|---:|---:|
| Append one event to a new stream | 2.427 ms | 4.310 ms | 9.1 KB | 14.59 KB |
| Append ten events in one call | 2.579 ms | 5.074 ms | 31.65 KB | 25.34 KB |
| Read a stream of 1,000 events | 1.932 ms | 4.734 ms | 734.9 KB | 1150.71 KB |

Run on 2026-10-26:

- AMD Ryzen 7 7840HS, 8 cores, Windows 11
- .NET 10.0.14, BenchmarkDotNet 0.15.8, 3 warmup and 15 measured iterations
- PostgreSQL 18 (alpine image) and SQL Server 2022 (`mssql/server:2022-latest`) in Docker 29.7.2 on the same laptop, started by Testcontainers

The PostgreSQL numbers are a little slower than the first run on 2026-10-05 (1.789 ms for one event then). Same code and machine, so compare the two engines within one run, not across dates.

## What the numbers say

An append is a fixed number of round trips whatever the batch size: open a pooled connection, begin, take the append lock, check the stream version, insert the batch, commit. Callers that have several events for the same stream should hand them over in one call.

That only held for PostgreSQL at first. The SQL Server store sent one insert per event, and ten events took 10.579 ms against 4.670 ms for one. It now inserts the whole batch in one `insert ... select ... order by` (500 rows per statement, which stays under SQL Server's 2,100 parameter limit), and ten events take 5.074 ms. The `order by` is there because SQL Server only promises identity values in a given order for an insert with a select and an order by, and positions inside one append must follow the versions.

A single append on SQL Server costs about 1.9 ms more than on PostgreSQL with the same round trips. I haven't profiled where that goes yet; `sp_getapplock` is a stored procedure call where PostgreSQL takes an advisory lock inside a plain select, and that is the first thing I'd measure.

The append lock makes appends run one at a time across all streams, on both engines. I haven't measured concurrent appends yet, but on this machine a single database should top out near 400 appends a second on PostgreSQL and near 230 on SQL Server. That is plenty for this project. The reason for the lock is in the post on [append order](https://softvasco.github.io/2026/10/event-store-append-order/).

Reading 1,000 events takes about 1.9 µs per event on PostgreSQL and 4.7 µs on SQL Server, JSON deserialization included. SQL Server keeps the event JSON in `nvarchar(max)`, which is UTF-16, so mostly ASCII JSON comes back at about twice the bytes, and the read allocates about 1.15 KB per event against 0.73 KB. With snapshots every 100 events (the default), loading an account reads at most 99 events after the snapshot, so the gap is small in practice.

The databases run in Docker on the same machine, so network latency is close to zero. Against a database on another host, every round trip adds to the append time.

## Running them

```bash
dotnet run -c Release --project benchmarks/LedgerCore.Benchmarks -- --filter '*'
```

Needs Docker. Each benchmark starts its own database container, so a full run takes a few minutes. `--filter '*SqlServer*'` or `--filter '*Postgres*'` runs one engine.
