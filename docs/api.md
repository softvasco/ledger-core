# HTTP API: contract and versioning

The API has one version today. There is no version in the URLs, and the OpenAPI document is served at `/openapi/v1.json`. This page says what I treat as the contract, which changes are safe, and what I'd do when one isn't.

## What the contract is

A client depends on more than the routes and the JSON shapes. For this API the contract is:

- Routes, methods and the status codes each endpoint lists in the OpenAPI document.
- Request and response bodies, including which fields are required.
- The `code` extension on problem details (`account.insufficient_funds`, `idempotency.key_reused`, ...). Clients are told to branch on it ([ADR-0008](adr/0008-problem-details-for-every-error.md)), so a renamed code breaks them as much as a renamed field.
- The `Idempotency-Key` rules: which endpoints honour it, and that the same key with a different request is a 422 ([ADR-0009](adr/0009-idempotency-keys-for-post.md)).
- Money as a JSON number with at most the currency's minor units, and a separate ISO 4217 `currency` field.

mcp-kit is being built to expose these endpoints as MCP tools, with the tool schemas generated from the same request types. A change to the HTTP contract will be a change to the tools, and an agent's prompt or a saved conversation can depend on a field name the same way client code does.

## Changes that don't need a new version

- A new endpoint.
- A new optional field in a request, as long as leaving it out keeps the old behaviour.
- A new field in a response. Clients have to ignore fields they don't know; System.Text.Json does by default.
- A new status code or problem `code` for a case that used to be a 500 or couldn't happen before.
- Looser validation, for example accepting a currency that used to be refused.

## Breaking changes

- Removing or renaming a field, an endpoint or a problem code.
- Making an optional request field required, or changing a field's type or format.
- Changing the status or `code` an existing case answers with. A 409 that becomes a 422 breaks every client that retries on 409.
- Stricter validation of input that used to be accepted.
- Changing what an existing field means, for example a balance that starts to include pending amounts. This one is the worst, because nothing fails to parse.

## When a breaking change is needed

The new version goes in the path: `/v2/accounts`. I picked the path over a header or a media type for three reasons:

- It shows up in access logs, traces and error reports without anyone having to log a header.
- An MCP tool runs one endpoint, so each version is its own endpoint with its own tool name and schema. A header-based version would put two contracts behind one tool.
- It needs nothing beyond what the API already uses. `/v2` is a route group next to the current one, with its own `WithIdempotencyKeys()` and its own OpenAPI document (`/openapi/v2.json`). I'd only add Asp.Versioning if there were several versions alive at once with real differences to report.

The current routes would stay where they are as v1. Moving them under `/v1` would itself break every client, so they keep working at the unprefixed path for as long as v1 is supported.

Both versions run against the same domain and event store. The version is a translation at the edge: a v1 handler maps its request into the same command a v2 handler sends. Events are never versioned by the API version. If an event's shape changes, that needs upcasters in the store ([ADR-0002](adr/0002-event-sourcing-for-the-ledger.md)), which is a separate problem.

## Retiring a version

When v1 is going away, its responses get the `Deprecation` header (RFC 9745) with the date it was deprecated, and a `Sunset` header (RFC 8594) with the date it stops answering, plus a `Link` to this page. The OpenAPI document marks its operations as deprecated, and the matching MCP tools get a note in their description so an agent's user sees it.

I'd keep a deprecated version for at least six months. There are no external clients yet, so this is the rule I'd start from rather than one I've had to defend.
