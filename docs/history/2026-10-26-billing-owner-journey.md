# Billing owner journey and signed-event delivery

Continues [the billing foundation](2026-10-26-billing-foundation.md) under
[ADR 0082](../adr/0082-billing-host-and-owner-journey.md).

Implemented disabled-by-default hosted Stripe composition, explicit operator settings/secrets, owner-only billing
and operation-recovery routes, a durable signed-event inbox with lease/retry handling, canonical reconciliation,
once-only customer portal creation, and a lazy-loaded Billing screen. Usage and compute views show the purchased
allowance alongside the owner's smaller safety limits. Merchant configuration and real launch prices remain unset.

The UI now presents the exact plan, monthly renewal and UTC-month allowance reset before explicit consent.
Reload retains the review and clears consent. A missing approval reply recovers its saved operation instead of
creating another checkout. Return links select the original company. Overdue/canceled subscriptions remain visible,
and payment verification controls new-work admission. A failed initial load offers a clear retry.

E2E exposed an inherited checkbox layout that stacked consent text below the input. Billing now explicitly sets
its checkbox row and header layout, removes the redundant new-subscription status card during review, and brings
the review into view. Desktop and 390px mobile screenshots were inspected in dark mode, plus mobile light mode.
An oversized webhook request also exposed HTTP/1 connection reuse after an unread rejected body; rejection now
closes that connection. Host disposal releases scheduler ownership even if startup never completed.

Verification:

- Company tests: **39 passed** across billing state, delivery, HTTP composition, usage and settings. They include
  owner-only GET/operation access, normal-route CSRF enforcement, accepted signed bodies above the normal API
  limit, 413 rejection above 256 KiB, duplicate delivery, unopened-company recovery, and explicit portal replacement.
- Integration regressions: **57 passed**, including Stripe transport/signatures and browser/script purchased
  admission. Checkout and portal returns carry the original company. All provider responses are fictional.
- Migration/legacy checks: **13 passed**; this selection overlaps the company suite. Migration 26 and the older
  downgrade fixtures preserve prior records and migration checksums.
- Chrome E2E passed with two companies and exactly two fictional checkout POSTs. It covers initial-load retry,
  exact price and consent, reload, dropped approval replies, duplicate signed events, original-session recovery,
  account portal replacement, unpaid usage, cancellation, and mobile overflow/checkbox alignment. No Stripe link
  was navigated and no live payment request was made.
- Company UI recovery and currency-format checks passed; frontend typecheck/build and backend builds passed.
  The pre-existing main-bundle size warning remains. C# and frontend formatting and whitespace checks passed.

Compact logs, JSON receipt and five screenshots are under ignored `test-results/billing-*`. The Chrome fixture
waited for its own processes to exit before removing its temporary directory. Free space was above 315 GiB.
Existing dirty work and the unrelated running application were preserved; nothing was committed, deployed or charged.

This is fictional local product proof. Real hosted Stripe test-mode checkout/portal acceptance, merchant setup,
commercial/tax/refund terms, operational billing-inbox tools/retention, BYOK and full operating-cost accounting
remain pending. The full three-phase product goal remains active.
