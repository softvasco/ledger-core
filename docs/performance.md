# Performance

Numbers for the PostgreSQL event store, from `benchmarks/LedgerCore.Benchmarks`. They are a baseline to compare later changes against, not a claim about production throughput.

## Results

| Benchmark | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| Append one event to a new stream | 1.789 ms | 0.026 ms | 9.07 KB |
| Append ten events in one call | 2.229 ms | 0.019 ms | 31.61 KB |
| Read a stream of 1,000 events | 1.779 ms | 0.023 ms | 672.3 KB |

Run on 2026-10-05:

- AMD Ryzen 7 7840HS, 8 cores, Windows 11
- .NET 10.0.12, BenchmarkDotNet 0.15.8, 3 warmup and 15 measured iterations
- PostgreSQL 18 (alpine image) in Docker 29.7.2 on the same laptop, started by Testcontainers

## What the numbers say

An append is a fixed number of round trips whatever the batch size: open a pooled connection, begin, take the append lock, check the stream version, insert the batch, commit. That is why ten events cost only about 0.44 ms more than one. Callers that have several events for the same stream should hand them over in one call.

The append lock makes appends run one at a time across all streams, and it is held for most of those round trips. I haven't measured concurrent appends yet, but I expect a single database to top out near 1 / 1.8 ms, about 550 appends a second on this machine. That is plenty for this project. The reason for the lock is in the post on [append order](https://softvasco.github.io/2026/10/event-store-append-order/).

Reading 1,000 events takes about 1.8 µs per event, JSON deserialization included, and allocates about 690 bytes per event. With snapshots every 100 events (the default), loading an account reads at most 99 events after the snapshot.

The database runs in Docker on the same machine, so network latency is close to zero. Against a database on another host, every round trip adds to the append time.

## Running them

```bash
dotnet run -c Release --project benchmarks/LedgerCore.Benchmarks -- --filter '*'
```

Needs Docker. Each benchmark starts its own PostgreSQL container, so a full run takes a few minutes.
