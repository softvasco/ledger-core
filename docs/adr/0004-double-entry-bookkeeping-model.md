# 4. Double-entry bookkeeping model

Date: 2026-10-01

## Status

Accepted

## Context

ADR-0002 says the ledger is event sourced. It doesn't say what a movement of money looks like. That shape ends up in every event, every projection and every API payload, so it is expensive to change later.

The questions I had to answer:

- Is a movement a single "transfer from A to B" record, or a set of debit and credit lines?
- Are amounts signed, or always positive with a side?
- What has to balance, and when is it checked?
- What about an entry that touches two currencies, like an FX trade?
- Where does a balance live?

## Decision

A movement is a `JournalEntry` made of two or more `Posting` lines. Each posting names one account, a side (debit or credit) and an amount. A transfer between two customers is two postings. A transfer with a fee is three: one debit on the payer, one credit on the payee, one credit on the fee income account. A single "from A to B" record can't express the fee without a second record, and then the two records can drift apart.

Posting amounts are always greater than zero, and the side says which way the money moves. A signed amount would give two ways to write the same line (a negative debit and a positive credit), and every report would have to agree on which one is meant. With the side as a separate field there is only one way.

An entry balances or it doesn't exist. `JournalEntry.Book` checks that total debits equal total credits and returns `ledger.unbalanced` otherwise (ADR-0003). There is no draft or half-booked state to clean up later.

The check runs per currency. An FX trade is one entry: EUR debited and credited on one pair of accounts, USD on another pair, each currency balanced on its own. I didn't want to convert at a rate inside the check, because then the rate becomes part of whether the books balance, and rates get corrected after the fact. The gain or loss on the trade is its own posting against an FX result account.

Amounts are `Money`, so a posting can't carry more decimals than its currency settles in. Rounding happens before booking (fee and interest calculations), never inside it.

The entry is the unit of atomicity: all its postings are booked together or none are. The booking time comes from `TimeProvider`.

Balances are not a field on the account. An account's balance is the sum of its postings, and the read side keeps it as a projection. The `Account` aggregate holds what has to be true before a posting is accepted (open, frozen, closed, currency). Checks that need the balance at write time, such as available funds, are decided with the transfer process manager, so I don't fix that here.

## Consequences

- Money can't appear or disappear through a booking. The property tests in `JournalEntryProperties` generate random balanced entries, check they book, and check that one minor unit off or one posting missing gets refused.
- Fees, splits, FX and reversals use the same model. A reversal is a new entry with the sides swapped, never an edit.
- Reports have to know each account's normal side (a customer deposit is a liability for the bank, so credits increase it). That belongs to the chart of accounts, which doesn't exist yet.
- More rows than a single transfer record: at least two postings per movement. That is the normal cost of double entry and not one I'd try to avoid.
