# 0049: Sign in with Google for Analytics and Search Console

Status: Accepted. Extends 0029.

## Context

Google Analytics and Search Console were read only with a service account. Setting that up took the owner several
consoles: create a project, enable APIs, create a service account and key, then share the GA4 property and the
Search Console site with the account's email. They asked for one-click connecting, like GitHub's.

HireZero cannot ship its own Google OAuth app for these scopes. A publisher's app needs Google's verification. The
owner's own OAuth client, created once in their Google Cloud project, needs no verification for their own use.

## Decision

- **Sign in with Google**, as the owner, with their own OAuth client (type "Desktop app"):
  - The client ID is a Google setting (`oauthClientId`); the client secret is a key
    (`HIREZERO_GOOGLE_OAUTH_CLIENT_SECRET`).
  - The flow is authorization code with PKCE (S256), `access_type=offline` and `prompt=consent`, for the same two
    read-only scopes the service account used.
  - Google returns to `http://<local host>/oauth/google/callback`. Only a pending sign-in from this host (a one-time
    state, ten minutes) is accepted.
  - The refresh token is stored encrypted as `HIREZERO_GOOGLE_OAUTH_REFRESH_TOKEN`, recorded as
    `integration.connected`, and switched on live. Access tokens come from Google's token endpoint only, and are
    cached until shortly before they expire.
- **The sign-in is preferred** when present; a service account key still works without it.
- **Pick, don't type.** After signing in, HireZero lists:
  - the GA4 properties the account can read (Analytics Admin API, `accountSummaries`);
  - its verified Search Console sites (`sites.list`).

  The owner picks one of each. A list that cannot be read (for example, the Admin API is not enabled) comes back
  empty with the reason.
- **Where it appears:** in a Connect card for Google Analytics and Search Console, and on the Integrations page.
  The setup steps lead with this path: enable the three APIs, publish the consent screen, and create the Desktop
  client.

## Consequences

- The consent screen must be **In production**. Unverified is fine for the owner's own account; in testing mode,
  Google expires refresh tokens after seven days.
- Sign-in is local mode only, like other pasted keys. Signing out removes the refresh token.
- Untested against Google itself until the owner first signs in: in particular, that Google accepts the loopback
  redirect's path for a Desktop client.
