# 3. Results for business rule failures, exceptions for bugs

Date: 2026-09-30

## Status

Accepted

## Context

The first aggregates threw exceptions for everything: a null argument, an unknown freeze reason, freezing a closed account, booking an entry that doesn't balance. Those are two different kinds of failure.

A null argument is a bug in the caller. Nobody should branch on it, and a stack trace is the most useful thing it can produce.

"This account is closed" or "debits don't match credits" is a normal answer from the domain. An API has to turn it into a 409 or a 422 with a clear message, and a batch job has to log it and move on to the next line. Using exceptions for that means try/catch around every command, exception types becoming part of the public contract, and the cost of throwing on a path that can be hit thousands of times in an import.

Options:

1. Keep exceptions and catch specific types at the edge.
2. Return a result type for business rule failures and keep exceptions for bugs.
3. Use a library (FluentResults, ErrorOr, OneOf).

## Decision

Option 2. Operations a business rule can refuse return `Result` or `Result<T>`. A failure carries a `DomainError` with a stable code (`account.invalid_state`, `ledger.unbalanced`) and a message for people. Each area keeps its errors in one place (`AccountErrors`, `LedgerErrors`), so the codes callers can see are easy to find.

Argument validation (null, default ids, undefined enum values, amounts with too many decimals) still throws. Reading `Value` on a failed result throws too, because that is a caller bug.

I didn't take a library because the type is about forty lines and I want no dependency in the domain project. If I end up needing the map/bind helpers these libraries have, I'll look at the question again.

## Consequences

- Every refusal is visible in the signature. Once a caller checks `IsSuccess`, the nullable annotations tell the compiler whether `Error` is set.
- The API layer maps error codes to HTTP status codes in one table instead of one catch block per exception type.
- A refused command raises no event, and the tests check that.
- Callers can drop a `Result` on the floor without a warning. I'll add an analyzer rule for that later if it turns into a problem.
