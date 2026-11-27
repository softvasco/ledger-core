# 18. OIDC sign-in for the back office, roles from the token

Date: 2026-11-27

## Status

Accepted

## Context

The back office shows every account and its history, so it can't stay open to anyone who reaches the port. Whoever runs it will already have an identity provider: Entra ID in most of the UK .NET contract ads I looked at this month, Keycloak or something else elsewhere. The admin shouldn't care which.

The pages render statically on the server (ADR 0017), so sign-in happens on normal HTTP requests and `HttpContext` is there while a page renders.

## Decision

- Cookie authentication for the session, OpenID Connect for the challenge. Authorization code flow with PKCE, no tokens saved in the cookie, because the admin doesn't call anything with the user's token yet.
- The admin only reads `Oidc:Authority`, `Oidc:ClientId` and an optional secret. It fails at startup if the authority isn't an absolute URL or the client id is missing.
- Roles come from a `roles` claim in the ID token. `MapInboundClaims` is off so the claim keeps that name. Entra ID app roles already arrive as `roles`; in Keycloak a protocol mapper on the client puts realm roles there.
- Two roles: `ledger-viewer` reads, `ledger-operator` reads and will do the admin actions when they come. Pages ask for the viewer policy, which either role passes.
- A fallback policy requires a signed-in user on every endpoint without its own rule. Health probes, static assets, the error page and the not-found page are anonymous on purpose. A signed-in user without either role gets `/access-denied`, not a loop back to the provider.
- Sign-out is a POST with the antiforgery token. Minimal APIs only check the token when they bind a form, so the endpoint validates it itself; a test showed the attribute alone let a POST without the token through.
- Locally, the AppHost runs Keycloak with `src/LedgerCore.AppHost/keycloak/ledger-realm.json`: the `ledger-admin` public client and three users (`viewer`, `operator`, and `nobody` with no role, each with their name as password). The admin gets only the issuer URL and the client id from the AppHost.

## Consequences

- Moving to Entra ID is configuration: point `Oidc:Authority` at the tenant, register the client and define the two app roles there.
- The realm file has fixed redirect URIs for `http://localhost:5175`, the admin's launch profile port. A different port needs the file changed too.
- The tests replace the provider's metadata with a static configuration, so they check the redirect, the roles and the anonymous paths without Keycloak. The full round trip (login page, form post back, cookie, page) was checked against the realm in a Keycloak 26.4 container.
- The API itself still has no authentication. Scopes and roles on the API come with its own task, and then the admin will need to keep the access token.
