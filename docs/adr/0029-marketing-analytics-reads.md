# 0029: Marketing analytics reads

Status: Accepted (Phase 4).

## Context

Phase 4 rebuilds Marketing on the employee contract, starting with reads of web analytics and search performance:
Google Analytics 4 and Google Search Console. The owner prefers not to tie HireZero to one vendor, and checks must
not make live calls.

## Decision

- **Neutral contracts.** `HireZero.Tools.Analytics` defines what Marketing needs, not what Google offers.
  - `analytics.status` reports which analytics source is connected.
  - `analytics.traffic` reads sessions, users, new users, page views, key events and engagement rate for a date
    range, as a total and optionally broken down by date, channel, landing page, device, country or source.
  - `analytics.search` reads clicks, impressions, click-through rate and average position, as a total and broken
    down by query, page, date, country or device.

  All three are Read tools. Another provider (Matomo, Plausible, Bing Webmaster Tools) could implement the same
  contracts with no change to Marketing.
- **Google implements them.** `HireZero.Integrations.Google` serves `analytics.traffic` from the GA4 Data API
  (`runReport`) and `analytics.search` from the Search Console API (`searchAnalytics.query`).
  - It authenticates as a service account. The key (JSON) is the secret `HIREZERO_GOOGLE_SERVICE_ACCOUNT`, read
    at use ([ADR 0027](0027-secrets-from-files.md)).
  - It signs a short-lived assertion (RS256) and exchanges it for a read-only token. The scopes are
    `analytics.readonly` and `webmasters.readonly`.
  - There is no Google SDK. Requests go only to `oauth2.googleapis.com`, `analyticsdata.googleapis.com` and
    `searchconsole.googleapis.com`, without redirects. A key file naming another token endpoint is refused.
  - The GA4 property and the Search Console site are set in `<data root>/google.json`, as other integrations do.
- **A fictional stand-in.** With `HIREZERO_SYNTHETIC_ANALYTICS=1`, a synthetic provider serves the same contracts
  with deterministic fictional data, labelled as such in every report. It is for demos and browser checks. Unit tests
  drive the Google provider through a fake HTTP handler with fictional API responses and a generated key. No check
  calls Google.
- **Marketing reads deterministically and judges through review.**
  - The `analytics-read` action reads the last 7, 28 or 90 complete days and the period before. Search
    Console's period ends three days earlier, because its data arrives late.
  - The action computes the changes between the two periods. It flags unusual days in sessions, key events and
    search clicks with a robust score: the median and median absolute deviation of the previous period's days of
    the same kind (weekday or weekend, so an ordinary weekend is not flagged), flagging |z| of 3.5 or more. A kind
    needs at least four baseline days to be judged.
  - The output is an artifact for the owner's review ([ADR 0011](0011-employee-actions.md)). Query and page text
    in it is untrusted third-party data.
  - The measured `kpi-digest` skill writes the digest only from approved evidence. Possible explanations are
    labelled as hypotheses, and it never adds numbers that are not in the evidence.

## Consequences

Marketing gains its first external reads without owning any vendor code. Connecting Google needs a service account
with Viewer access to the GA4 property and the Search Console site, plus two ids in `google.json`. Ads platforms and
email/CRM follow the same shape: neutral Read contracts first, writes later and only with approval.
