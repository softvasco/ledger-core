# 15. Rebuilding a projection in place, from inside the API

Date: 2026-10-29

## Status

Accepted

## Context

A projection only moves forward from its checkpoint (ADR 0012). When its code changes, for example a new column on the statement lines, the rows it already wrote stay as they were. On SQL Server they also survive restarts (ADR 0013), so the only way to rebuild them was to drop the tables by hand and restart.

I first planned this as a console command. It wouldn't work for two of the three setups: with no database, or on PostgreSQL, the read model lives in the API's memory, so a second process would rebuild a copy that dies when it exits. It was also unsafe on SQL Server, because the running API's catch-up would apply the same events into the tables while the command was refilling them, and the two would race on the statement line keys.

## Decision

- A projection that can be emptied implements `IRebuildableProjection.ResetAsync`. The in-memory one clears its dictionaries. The SQL Server one deletes its rows and its checkpoint row in one transaction, so a crash can't leave a checkpoint over empty tables.
- `ProjectionRunner.RebuildAsync(name, progress)` takes the same lock as the background catch-up, resets the projection, sets its checkpoint to 0 when the runner keeps it, and replays the store. It reports progress once per checkpoint batch (100 events) and once at the end.
- The API runs it: `POST /admin/projections/{name}/rebuild` answers 202 and starts the rebuild in the background. `GET /admin/projections/{name}/rebuild` returns the state (`running`, `completed`, `failed`), the events applied so far and the last position. A second POST while one runs is a 409 with `projection.rebuild_running`.
- The routes only exist when `Admin:Enabled` is true. The API has no authentication yet, and an open endpoint that empties the read model is not something to ship by default.

## Consequences

- While a projection rebuilds, it is empty or half full, so `GET /accounts/{id}` can answer 404 for an account that exists. Building into new tables and swapping them at the end would avoid that, at the cost of a second schema per projection. I left it out until a rebuild is slow enough for the gap to matter.
- The catch-up pass waits on the lock during a rebuild, so new events reach the read model only when the rebuild ends. The rebuild reads to the end of the store, so it picks up most of them itself.
- If a rebuild fails, the checkpoint stays on the last event it applied and the next catch-up pass carries on from there. The status says `failed` with the error message.
- The status lives in memory, so a restart forgets it. The rows are what matter, and they are in the read model.
- With more than one API instance, each has its own lock. A rebuild on SQL Server should then run with one instance up, until there is a lock in the database.
