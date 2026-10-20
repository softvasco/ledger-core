# 11. Transfers run as a process manager over two account streams

Date: 2026-10-20

## Status

Accepted

## Context

A transfer changes two accounts. Each account is its own event stream with its own expected version (ADR-0006), and the event store has no transaction across streams. So a transfer can't be one append.

The options I looked at:

- One stream for both accounts. Every transfer would conflict with every other change on either account, and the balance check would move away from the account.
- A database transaction over both appends. Ties the model to one store, and the event store appends to one stream per call.
- A transfer with its own stream that records each step, and drives the two accounts one at a time. A failure halfway is a state the transfer knows about, not a mystery.

## Decision

`Transfer` is an aggregate with its own stream. Its events are the steps: `TransferRequested`, `TransferSourceDebited`, then `TransferCompleted`, or `TransferFailed` when the source refuses and `TransferCompensated` when the destination refuses after the source paid.

`TransferProcessManager` runs it. It withdraws from the source, records the debit, deposits to the destination and records the end. When the deposit is refused (a closed account, a different currency) it deposits the amount back on the source and records the compensation with the destination's error code. Each step is saved before the next starts, so a manager given a loaded transfer continues from the last recorded step.

`StartTransfer` returns the refusing account's code, so a refused transfer turns into the same problem as a refused withdrawal.

## Consequences

- There is a moment where the money has left the source and not reached the destination. The read side sees the debit before the credit. Statements need to show the transfer as pending in between.
- A crash between the withdrawal and `TransferSourceDebited` is not covered yet. On restart the transfer still says Requested, and running it again would withdraw twice. The fix is for the account events to carry the transfer id, so the manager can see the debit already happened. That comes with the outbox work and the transfer endpoints.
- If the refund is refused too (the source was closed in between), the manager throws and the transfer stays at SourceDebited. That needs a person, and I'd rather leave it visible than invent a third account to park the money in.
- Nothing runs unfinished transfers on startup yet. The outbox dispatcher will.
