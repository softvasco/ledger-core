# 6. The account stream keeps the balance for the funds check

Date: 2026-10-07

## Status

Accepted. Narrows the last paragraph of ADR-0004.

## Context

ADR-0004 says a balance is not a field on the account: it is the sum of postings, kept by the read side, and checks that need the balance at write time were left for the transfer process manager.

Withdraw is the first command that needs that check. It has to refuse a withdrawal that would take the account below zero, and the refusal has to be safe when two withdrawals race. The read side can't decide this. A projection lags behind the event store, so two withdrawals of 80 against a balance of 100 could both read 100 and both pass.

The options I looked at:

- Check against the read model and accept the race. Simple, and wrong for money.
- Check against the read model and lock it during the command. Puts a lock on the read side and makes it part of the write path.
- Keep the balance on the account aggregate. The account stream already has optimistic concurrency (expected version on append), so two racing withdrawals can't both be stored: the second gets a `ConcurrencyConflictException` and has to reload.

## Decision

`Account` keeps a `Balance`, changed only by `MoneyDeposited` and `MoneyWithdrawn`. `Withdraw` returns `account.insufficient_funds` when the amount is larger than the balance, and records nothing. The balance is part of the snapshot.

A frozen account still takes deposits and pays nothing out. A hold is about money leaving.

This balance exists for the write-side check. Statements, history and reporting still come from the read side, and the journal entries from ADR-0004 stay the model for movements between accounts: when transfers arrive, a deposit becomes an entry against a settlement account, and the account events record the effect on that one account.

## Consequences

- Concurrent withdrawals on one account are serialised by the stream version. The loser retries with fresh state. That is a conflict per hot account under load, which is fine for customer accounts and something to measure for a busy internal account.
- Snapshots changed shape. Old snapshot rows without a balance fail to read, because the JSON context now requires every constructor parameter, and a failed read is a cache miss (the account replays from its events).
- `Money` is now in events, stored as `{"amount": 12.50, "currency": "EUR"}` and validated again through `Money.Of` when it is read back.
- The balance lives in two places, the aggregate and the read model. Both are built from the same events, and a test on the read model will compare them once it exists.
