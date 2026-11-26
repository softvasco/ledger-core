# 17. Static server rendering for the back office

Date: 2026-11-26

## Status

Accepted

## Context

The back office (`src/LedgerCore.Admin`) is a Blazor Web App, so every page can render in one of four ways: static on the server, interactive on the server over a SignalR circuit, interactive in WebAssembly, or Auto, which starts on the server and moves to WebAssembly once the runtime has downloaded.

What the pages do today is read: a list of accounts a page at a time and one account with its event timeline. There are no forms yet. Every number on them comes from the API, and the API is the only thing that touches the stores. Who uses it is a few operators, not the public.

## Decision

- Pages render statically on the server. No page sets `@rendermode`, so each request runs the component once, writes HTML and forgets it.
- Paging lives in the query string (`accounts?page=2`, `accounts/{id}?after=50`) through `[SupplyParameterFromQuery]`, and the pager is plain links. A page can be bookmarked or sent to someone, and the back button works.
- `blazor.web.js` stays on the page for enhanced navigation: clicking a link fetches the next page and patches the DOM, so it doesn't feel like full reloads, but there is no circuit behind it.
- A missing account calls `NavigationManager.NotFound()`, so the response is a real 404 rendered by the router's not-found page.
- Interactive server components stay registered in `Program.cs`. When a page needs a live control (a confirm dialog before an admin action, say), that one component opts in with `@rendermode InteractiveServer` and the rest of the page stays static.
- WebAssembly is out for now. It would mean shipping the runtime to the browser and calling the API from there, which brings CORS and tokens in the browser for pages that only show data.

## Consequences

- No server memory per open tab and nothing to reconnect. `ReconnectModal` only matters once a component goes interactive.
- Nothing updates by itself. A balance changes when the operator reloads or pages, and the account page says so when the read model hasn't caught up yet.
- Static rendering is what the sign-in task expects: the OIDC handler and cookie auth work on normal requests, and `HttpContext` is there during rendering. An interactive circuit would need the auth state carried over explicitly.
- bUnit tests render the components without a server, so they check the markup and what the page asks the API for. Status codes come from the router at runtime, so the tests only check that the page reports the not-found case; the 404 itself was checked by running the app.
