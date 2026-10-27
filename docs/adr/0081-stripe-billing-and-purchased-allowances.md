# 0081 — Stripe billing receipts and purchased allowances

Status: accepted as a locally verified foundation; host activation and commercial terms remain pending. Date: 2026-10-26.

## Decision

Implement Stripe as the first configurable payment adapter behind a provider-neutral core contract. The owner
has not selected launch prices or connected a merchant account. There is no default paid catalog and no new
payment call in the running host. Fictional amounts in tests are not proposed launch terms.

Use hosted subscription Checkout for an explicit existing monthly price, and a separately configured Stripe
customer portal. The integration accepts a single licensed, per-unit price with quantity one. It does not create
products, prices, discounts, trials, tax policy or subscription changes. Before creating checkout it verifies the
merchant account, mode, exact current price, and still-current owner authority. A fixed return URL and provider
URL validation keep payment navigation within the configured application and Stripe destinations. Pin requests
and webhook parsing to `2026-09-30.endive`; changing this version requires a reviewed compatibility change.

Core owns company approval, durable dispatch, payment evidence and admission. Integrations own transport and
provider-specific parsing. Roles do not acquire payment tools. No provider SDK or runtime pin was upgraded.

## Approval and uncertain outcomes

- An operator supplies a bounded catalog of named plans, exact prices and UTC-month AI/compute allowances.
  The catalog is immutable within a store instance. Configuring it durably marks that company as requiring
  billing. Removing configuration or restarting without credentials cannot silently restore free execution.
- `billing.prepare` creates a 30-minute review containing the price, allowance, provider identity and digest.
  Only an active owner may approve its exact version/digest with `approve-subscription`. Approval and queue
  state commit together. Each company can have only one unresolved checkout or subscription.
- A transactional dispatch claim is taken once before POST. The integration also supplies a stable idempotency
  key, but never treats that key as permission to replay an uncertain request. Known preflight refusals can close
  unsent attempts; interruption, malformed POST responses and uncertain provider errors keep the original attempt
  unresolved. Reopening a store cannot obtain another dispatch.
- Recovery reads the original checkout. If its identifier was lost, a verified event or operator recovery must
  identify that original session; recovery still verifies company, operation, plan and provider bindings. It
  cannot create another checkout. A premature recovery read cannot discard a late confirmed creation receipt,
  and a late creation receipt cannot replace newer canonical evidence.
- A provider-object claim table binds each customer to one company and each checkout/subscription to one
  company and approval. These identities cannot be reassigned by reusing a provider receipt. Confirmed
  cancellation, expiry or a proven unsent failure permits fresh approval. The original customer is reused.

## Payment verification and admission

Neither a success redirect, a checkout creation response nor a signed event alone grants an allowance. The
workflow reads the canonical checkout, subscription and its latest invoice, then reads the subscription again
to detect a renewal/cancellation racing the invoice read. Every receipt must agree on its approved company,
operation, price, plan, customer and subscription. A refresh identifier prevents older in-flight results from
overwriting a newer refresh. Failed verification leaves new admission closed.

Allowance requires a completed checkout, an active subscription with collection unpaused, a current service
period, and a matching paid invoice with nothing remaining. `trialing`, `past_due`, `unpaid`, incomplete,
canceled and paused subscriptions grant nothing. An active subscription scheduled to cancel at period end
retains its verified allowance within that period. Verification expires after at most one hour or at period end,
whichever is earlier. Host activation must supply regular read reconciliation and actionable owner status;
this foundation alone does not run that scheduler. Refund/dispute policy remains commercial integration work.

Purchased AI jobs/tokens and shared script/browser jobs/allocated seconds are enforced in the existing
transactional admission paths, independently of owner safety limits. Raising a safety limit cannot raise a
purchased allowance. Outstanding reservations carry across UTC-month boundaries. Browser admission includes
its cleanup allocation. Already authorized jobs may finish; payment changes do not erase usage or release
unknown reservations. Allowances reset by UTC calendar month, independently of the subscription's renewal date;
the eventual plan review must state this clearly. Changing this policy requires a new decision.

The exact approved catalog snapshot remains authoritative. Catalog/provider changes close admission until
explicit migration or renewed approval is implemented; this is not an automatic plan-upgrade mechanism.

## Webhook and storage boundaries

The Stripe adapter verifies raw request bytes with rotating signing secrets, bounded timestamp tolerance,
constant-time HMAC comparison, expected API version/mode and ordinary merchant scope. It rejects ambiguous
JSON and oversized bodies and returns only an event/object identifier and payload digest. Unknown verified
event types are ignored. Raw customer/payment bodies and secrets do not enter exception messages or audit
events. Event dates are not an ordering authority.

Migration 25 adds company checkout/state records and provider-object ownership claims. Earlier migration
checksums remain unchanged. A checkout URL remains in its owner-facing receipt until a canonical refresh
replaces it; host routes must enforce owner access and must not put these receipts in a general dashboard or
model context. No card data is stored. Existing local audit and database protection still apply.

Follow-up: [ADR 0082](0082-billing-host-and-owner-journey.md) composes the host, event inbox, portal and owner UI.
The original foundation slice below remains distinct from real merchant acceptance.

This slice does **not** register a payment provider with the host, expose billing routes, implement a webhook
inbox/deduplication worker, activate a customer portal, add Billing UI, or set up a live merchant account. Those
are required before product billing can be accepted. Launch pricing, tax/refund policy, test-mode hosted E2E,
BYOK terms, full operating-cost accounting and real paid conversion remain open.

Protocol references: [Stripe API versioning](https://docs.stripe.com/api/versioning),
[subscription events](https://docs.stripe.com/billing/subscriptions/webhooks),
[webhook verification and delivery](https://docs.stripe.com/webhooks),
[hosted Checkout creation](https://docs.stripe.com/api/checkout/sessions/create), and
[subscription receipts](https://docs.stripe.com/api/subscriptions/object).
