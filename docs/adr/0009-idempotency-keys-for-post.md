# 9. Idempotency keys for POST requests

Date: 2026-10-16

## Status

Accepted

## Context

A client that sends a deposit and loses the connection before the answer comes back can't tell whether the deposit happened. If it sends the request again, the account is credited twice. Commands are not idempotent by themselves: two deposits of 50 are two deposits.

The usual answer, used by Stripe and written up in the IETF draft for an `Idempotency-Key` header, is to let the client name the operation. The server runs the first request with that key and answers every repeat with the stored response.

## Decision

`IdempotencyMiddleware` handles POST requests that carry an `Idempotency-Key` header. Without the header a POST runs as before; the key is optional.

- A key is 1 to 255 characters. Anything else, or the header sent twice, is a 400 with code `idempotency.invalid_key`.
- The request is fingerprinted with SHA-256 over the method, the path and the body. The same key with another fingerprint is a 422 with code `idempotency.key_reused`, because the client is mixing two operations under one name.
- The first request claims the key before it runs. A repeat that arrives while the first is still running gets a 409 with code `idempotency.in_progress` and can retry.
- When the request finishes below 500, the status, content type, `Location` and body are stored. A repeat gets them back with `Idempotent-Replayed: true`. Refused commands (a 409 for insufficient funds, a 422 for a currency mismatch) are stored too: the retry should get the answer the first request got, even if the account has changed since.
- A 5xx or an exception releases the key. Nothing was promised to the client, so the retry runs for real.
- Stored responses are kept for 24 hours. A claim that never finished (the process died) can be taken over after one minute.

`IIdempotencyStore` sits in the application layer with two implementations. The in-memory one is for tests and local runs. `PostgresIdempotencyStore` keeps keys in an `idempotency_keys` table; the claim is one `insert ... on conflict do update ... where` that only overwrites an expired row, so two instances racing on the same key can't both win.

## Consequences

- Clients that want safe retries send a fresh key per operation (a UUID is fine) and reuse it only for retries of that operation.
- Keys are not scoped to a caller yet, because the API has no authentication. Once it does, the key has to include the caller, or two clients that pick the same key would see each other's responses.
- The response is buffered in memory to store it. Ledger responses are small, so that is fine here; an endpoint that streams large bodies would need to skip the middleware.
- The claim and the command are not in one transaction. If the process dies after the command commits and before the response is stored, the key frees up after the lock timeout and a retry runs the command again. Closing that gap means storing the response in the same transaction as the events, which waits for the outbox work.
- Expired rows stay in the table until a key is reused. A cleanup job is cheap to add when the table grows.
