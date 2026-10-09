# 7. Own command and query dispatcher instead of MediatR

Date: 2026-10-10

## Status

Accepted

## Context

The application layer sends commands (OpenAccount, Deposit, Withdraw) and queries (GetAccount) to handlers that live in the same assembly. Something has to find the handler for a message, run cross-cutting steps around commands (tracing, logging, validation), and keep the API and the MCP tools from knowing which class does the work.

In .NET that something has usually been MediatR. The options I looked at:

1. MediatR. From version 13 it is a commercial product with a paid licence above a revenue threshold. Version 12 and earlier stay under Apache 2.0, and new work happens on the commercial line. Using either means a licence question or an old dependency in a repo that other people might copy.
2. A source-generated mediator (martinothamar/Mediator, MIT). Same idea as MediatR, generated at compile time, so no reflection. Well made, and another dependency with its own conventions for handlers and pipelines.
3. Wolverine (JasperFx, MIT). A messaging framework with in-process handling as one feature. Far more than this needs: transports, durable inbox and outbox, its own code generation.
4. A dispatcher written here.

What the ledger actually needs from a mediator is small: one handler per message type, an ordered list of behaviors around commands, scoped resolution from the caller's DI scope, and a clear error when a handler is missing or registered twice.

## Decision

ledger-core has its own `CommandDispatcher` and `QueryDispatcher` in the application layer.

- One handler per message. Both dispatchers ask the container for every handler of the message type and throw unless there is exactly one. `GetRequiredService` would return the last registration and hide a duplicate, which is the bug I care most about here.
- Callers only hold `ICommand<TResult>` or `IQuery<TResult>`, so the closed handler type is built with reflection once per message type and cached as a small typed invoker. After the first call a dispatch is a dictionary lookup and a virtual call.
- Commands run inside `ICommandBehavior` instances, in registration order: tracing, logging, validation, then the handler. Queries have no behaviors. They don't change state, so there is nothing to validate as a command, and the HTTP span already covers them.
- `AddCommands` and `AddQueries` scan the given assemblies and register everything as scoped.

The dispatchers, their interfaces and registration are about 250 lines, with tests for the missing handler, the duplicate handler, behavior order and cancellation.

## Consequences

- No licence question and no old dependency. Anyone can copy the repo, or just the dispatcher, under MIT.
- Nothing about the dispatcher is hidden. The tracing and validation behaviors in this repo are written against an interface I control, and the ADRs and tests explain them.
- I own the maintenance. Features a library has and this doesn't (notifications to many handlers, streaming requests, pre and post processors) get added only when a use case shows up.
- Startup reflection. Scanning assemblies and building invokers on first use is fine for an API process. If ledger-core ever needs Native AOT, the scan and the cached `MakeGenericMethod` have to move to a source generator, and option 2 becomes the obvious comparison again.
