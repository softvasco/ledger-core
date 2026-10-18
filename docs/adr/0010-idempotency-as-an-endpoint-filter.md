# 10. Idempotency as an endpoint filter

Date: 2026-10-18

## Status

Accepted. Changes how ADR-0009 is wired; its rules stay the same.

## Context

ADR-0009 put idempotency in a middleware. mcp-kit exposes endpoints as MCP tools and runs them in-process by calling the endpoint's `RequestDelegate` with a fresh `HttpContext`. That call never goes through middleware, so a retried tool call would deposit twice even with a key. Agents retry more than most HTTP clients, so this is the caller that needs the protection most.

## Decision

The same rules now run as an endpoint filter, added to the `/accounts` group with `WithIdempotencyKeys()`. Filters are part of the endpoint's delegate, so they run for HTTP requests and for any host that calls the delegate directly.

- The fingerprint is the method, the path and the bound arguments serialized with the app's JSON options, not the raw body. The body has already been read by binding when a filter runs, and an in-process caller may build its request differently from an HTTP client.
- The filter executes the handler's `IResult` into a buffer, stores it, and returns an empty result. The filter refuses at startup an endpoint that doesn't return an `IResult`, because there would be no response to store.
- Parameters that come from DI, the `CancellationToken` and the `HttpContext` are left out of the fingerprint.

## Consequences

- Bodies that differ only in whitespace or property order now count as the same request. That is closer to what the client meant.
- A body that fails binding is a 400 before the filter runs, so it no longer claims the key. The retry with a fixed body gets a real run.
- Every endpoint that should honour keys has to opt in. Today that is the whole `/accounts` group; a new group needs `WithIdempotencyKeys()` as well.
- A tool call still has to carry the key. mcp-kit has to pass one as the `Idempotency-Key` header, which is its own change.
