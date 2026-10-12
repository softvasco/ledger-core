# 8. Problem details for every error response

Date: 2026-10-12

## Status

Accepted

## Context

The first endpoints answered errors in three shapes. Validation failures were `ValidationProblemDetails`. A refused command was a 409 with the raw `DomainError` as JSON. Not found was an empty 404, and so was an unknown route. Anything thrown below the endpoint (a command validator, a concurrency conflict from the event store) came out as a 500.

That last part was a real bug: a deposit of 0 got past the endpoint, the command validator rejected it, and the client got a 500 for its own mistake.

A client should parse one error shape and branch on one field.

## Decision

Every error response is an RFC 9457 problem (`application/problem+json`) written through ASP.NET Core's `IProblemDetailsService`, so each one also carries a `traceId`.

- A refused command keeps its `DomainError` code as a `code` extension. `DomainProblems` holds the only table from codes to status codes:

| Code | Status | Why |
|---|---|---|
| `account.not_found` | 404 | |
| `account.invalid_state` | 409 | works once the account is in another state |
| `account.insufficient_funds` | 409 | works after a deposit |
| `account.currency_mismatch` | 422 | can never work as sent |
| `ledger.too_few_postings`, `ledger.unbalanced` | 422 | can never work as sent |
| anything not listed | 422 | a new rule refuses input until it gets a row here |

- `ProblemExceptionHandler` maps the exceptions a client can act on: `CommandValidationException` to a 400 with errors grouped by field, `ConcurrencyConflictException` to a 409 with code `concurrency_conflict` (resending is safe), and `BadHttpRequestException` to its own status.
- Everything else is a 500 with no exception text. The details stay in the logs, matched by `traceId`.
- `UseStatusCodePages` gives empty 404s and 405s from routing the same shape.

I left `type` as the default `about:blank` link to the status code. A URI per error code only helps if it points to a page that explains the code, and that page doesn't exist yet. The `code` extension is the stable contract.

## Consequences

- Clients branch on `code` and never on `detail`, which is free text for people.
- Adding a domain error needs a row in `DomainProblems` unless 422 is right. A missed row still gives a sensible answer, not a 500.
- Overdrawing was already a 409 and stays one. Its body changes from `{ code, message }` to a problem with `code` and `detail`, which breaks any client that read `message`. Nothing outside the tests calls the API yet.
