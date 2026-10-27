# 0082: Billing host, signed event inbox and owner journey

Date: 2026-10-26

Status: Accepted; local fictional composition, merchant setup and hosted Stripe acceptance pending.

## Context

[ADR 0081](0081-stripe-billing-and-purchased-allowances.md) established payment contracts and purchased
allowance enforcement. Owners also need a reviewable checkout, reliable return path, payment recovery and
account management. A signed notification must survive a restart without becoming permission to repeat a charge.

## Decision

- The host composes Stripe only with explicit hosted identity, HTTPS public origin, merchant, test/live mode,
  portal configuration, catalog and separately supplied API/signing secrets. No launch prices are bundled.
  `HIREZERO_BILLING_ENABLED=0` prevents new checkout dispatch while retaining reconciliation and portal access.
- Only `POST /api/billing/stripe/events` uses provider signature authentication in place of browser cookie/CSRF
  checks, after listener/public-host validation. It accepts at most 256 KiB of raw JSON and verifies the pinned
  Stripe API version, mode, timestamp and signature before recording a bounded, content-free event notice.
  Oversized HTTP/1 requests close their connection; other browser routes retain their existing admission checks.
- Migration 26 adds a provider/event-ID inbox and company-scoped portal records. Acknowledgement follows durable
  enqueue. Duplicate delivery is deduplicated by stable identity, including when delivery serialization changes.
  Workers use five-minute leases and bounded read-only backoff; twelve failures retain a `needs-review` record.
  Unsupported signed event types are acknowledged without retention. The global inbox has no public HTTP listing.
- Canonical provider reads resolve the original company and checkout binding, then re-read current subscription
  and invoice state. Late events cannot revive an unpaid subscription or replace a newer checkout. Lost creation
  replies can recover their original session through a verified event; no recovery path POSTs checkout again.
  Known sessions are reconciled periodically, normally every ten minutes. Paid evidence expires within one hour
  or at the paid period end, whichever is sooner. A failed verification removes admission until verified again.
- Billing details, checkout reviews and saved billing-operation replies require an owner, including GET routes.
  Preparing a subscription shows the exact price, monthly renewal, allowance and separate UTC-month usage reset.
  A checkbox confirms the review; refreshed pages preserve the review but clear consent. Saved operation IDs
  reconcile a missing reply before another command can be submitted.
- Checkout and portal return URLs select the originating company. Redirects only request a canonical status check;
  they never grant allowance. Links open Stripe in a new tab. The app never accepts payment card details.
- Opening a customer portal is an owner command with a five-minute dispatch/link window. An unknown creation
  outcome requires an explicit new-link request. The original POST is not replayed. Configure the initial portal
  for payment details, invoice access and cancellation at period end; plan and quantity changes remain unsupported.
- Usage and compute views take the lower of purchased allowance and owner safety limits. Unverified required
  billing shows no new-work allowance. Billing offers a retry after initial load failure and refreshes saved status
  while open. Mobile review keeps checkbox and consent text together and brings the exact review into view.

## Operator setup and remaining acceptance

See [settings](../SETTINGS.md#billing) and [secrets](../SECRETS.md). The catalog is a bounded JSON array of
`BillingPlan` objects: `id`, `name`, `price` (`id`, `currency`, integer `unitAmount`, `interval: "month"`,
`intervalCount: 1`, `active: true`), and `allowance` (`jobs`, `tokens`, `computeJobs`, `computeSeconds`). Prices
must match the merchant's current Stripe price exactly; amount representation follows the provider's currency
rules. Changing a catalog invalidates old review digests. Existing subscription migration is separate work.

The Stripe event destination must use API `2026-09-30.endive`. Supported events are checkout session completed,
expired and async payment success/failure; subscription created, updated, deleted, paused and resumed; and invoice
paid, payment failed, payment action required, voided and marked uncollectible. The current and optional previous
signing secrets support rotation. See the exact type names in `StripeWebhook.cs` when configuring the destination.

Local tests use a fictional in-process provider with real signature verification, HTTP routing, durable storage
and browser UI. They do not contact Stripe. Live merchant setup, reviewed commercial terms, tax/refund decisions,
real hosted test-mode checkout and portal acceptance, operational inbox recovery/retention, BYOK terms and full
operating-cost accounting remain open. This does not complete the three-phase product goal.
