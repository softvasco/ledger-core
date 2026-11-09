# 16. Integration events in their own project, versioned by namespace

Date: 2026-11-09

## Status

Accepted

## Context

The events in the store are private to the ledger. I can change how they are written as long as I can still read the old ones (ADR 0002). Once other services consume events from a broker, that freedom goes: a consumer deployed months ago keeps reading whatever shape it was built against.

Publishing the domain events directly would tie the two together. They use value objects such as `Money` and `TransferId`, so a consumer would need the domain assembly. Some also carry too little: `TransferCompleted` in the domain only has the transfer id and the time, because the process manager already knows the rest.

## Decision

- `LedgerCore.Contracts` holds the published events. It references nothing else in the solution, so a consumer can take it without the domain.
- Each event is a record of plain types: `Guid`, `string`, `decimal`, `DateTimeOffset`, `int`, `long`, `bool`. A test fails if a property uses anything else.
- `[IntegrationEvent("ledger.transfer.completed", 1)]` sets the name on the wire, here `ledger.transfer.completed.v1`. The C# name can then change without breaking anyone.
- A breaking change is a new record in a new namespace (`Transfers.V2`), and the old one keeps being published until its consumers have moved. Adding an optional field stays in the same version.
- Amounts travel as JSON numbers from `decimal` with a separate currency code. `System.Text.Json` keeps the scale, so 100.10 arrives as 100.10.

## Consequences

- The mapping from domain to contract is code I have to write and keep. `TransferCompleted` needs the accounts and the amount from the process manager's state, so that mapping comes with the publisher.
- Publishing two versions at once means two messages per event during a migration. I prefer that to a consumer failing on a field it doesn't know about.
- Only three events are public for now: account opened, transfer completed and transfer failed. Withdrawals and deposits stay internal until something needs them.
- The tests check the rules, not each field. Guarding the actual JSON of each version against accidental edits is a separate job, done with schema snapshots.
