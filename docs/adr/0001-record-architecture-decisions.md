# 1. Record architecture decisions

Date: 2026-09-27

## Status

Accepted

## Context

A ledger lives for a long time, and a lot of its design only makes sense if you know what was traded away. In six months I won't remember why the event store is hand-written or why errors became result types, and anyone reading the code for the first time has even less to go on.

## Decision

I keep lightweight architecture decision records in `docs/adr/`, one Markdown file per decision, numbered in order: `NNNN-short-title.md`.

Each record has a status (proposed, accepted, superseded), the context, the decision and its consequences. Records are never rewritten after they are accepted. If a decision changes, a new record supersedes the old one and both link to each other.

A decision gets a record when it is hard to reverse, when it picks one reasonable option over another, or when someone reading the code would ask "why not X?".

## Consequences

- The reasoning sits next to the code and goes through review in the same pull request.
- Small, obvious choices stay out of here, otherwise nobody reads the records.
- Superseded records stay, so the history of how the design moved is visible.
