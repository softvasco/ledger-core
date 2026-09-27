# 2. Event sourcing for the ledger

Date: 2026-09-27

## Status

Accepted

## Context

A ledger has to answer three kinds of question, and all of them come up in banking every day:

- What is the balance now?
- What was the balance at the end of 31 March, as we knew it then?
- How exactly did this account get to this number?

The usual design stores the current state (an `accounts` table with a `balance` column) and writes an audit table on the side. The first question is easy. The other two depend on the audit table being complete and in step with the state, and nothing guarantees that. A missed audit write, a manual fix in production or a bug in a trigger, and the two stories quietly diverge.

Options I looked at:

1. State tables plus an audit table written in the same transaction.
2. State tables plus change data capture from the database log.
3. Event sourcing: the events are the only source of truth and state is derived from them.

## Decision

The ledger is event sourced. Every change to an aggregate (account opened, funds posted, account frozen...) is stored as an immutable event in an append-only stream per aggregate. Current state is rebuilt by replaying the stream, with snapshots for long streams. Reads that need to be fast or queryable (balances, statements) come from projections built from the events.

Appends use optimistic concurrency on the stream version, so two commands racing on the same account can't both win.

Which store to use (hand-written on PostgreSQL, Marten or EventStoreDB) is a separate decision, recorded later.

## Consequences

Good:
- The audit trail and the state can't disagree, because the state is computed from the audit trail.
- "As of" questions are a replay up to a point in time, not archaeology.
- New read models can be built later from the full history, including ones nobody thought of today.
- Events are a natural fit for integration: other services react to what happened instead of polling tables.

Costs I accept:
- Read models are eventually consistent. Anything that must be exact at write time (such as overdraft checks) is enforced inside the aggregate, not against a projection.
- Events are forever, so their schema needs care: additive changes, upcasters for old versions, and contract tests.
- Personal data must not go into events, because an immutable log and the right to erasure don't mix. Events carry ids; names and addresses live in a separate store that can be deleted.
- More moving parts than a CRUD table: projection runners, checkpoints, rebuilds. That is the price of the three questions above having trustworthy answers.
