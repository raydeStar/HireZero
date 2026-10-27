# Billing refresh UX — 2026-10-26

The billing owner journey exposed a reproducible timing bug: an older background read could fail after a
newer payment check verified the subscription, leaving a loading error over the active subscription.
The existing generation guard covered successful reads but not rejected reads.

Billing now rejects stale read errors as well as stale data. Explicit payment checks and billing commands
invalidate earlier reads when they start. Payment checks show `Checking payment…`, expose `aria-busy`, and
keep the action disabled until the check and saved-state read finish. Current read failures remain visible;
a successful explicit check clears them. This is a repair within ADR 0082, with no change to payment authority,
commercial terms, external replay policy or purchased allowance enforcement.

## Verification

- The new browser regression failed before the fix with a stale loading error above a verified subscription.
- The extended fictional desktop/mobile billing journey passed after the fix. It holds an older background
  read, verifies payment through a newer check, then releases the older failure. It also verifies visible check
  progress, duplicate-click prevention, current read failure/recovery, exact plan consent, saved checkout
  recovery, duplicate signed events, explicit replacement portal links, unpaid allowance and company isolation.
- Two focused Node checks passed: billing currency representation and company-scoped command recovery.
- TypeScript/Vite build, changed-file formatting, JavaScript syntax and whitespace checks passed. Vite retains
  its existing large-chunk advisory. The checking-state screenshot was visually inspected.
- The fixture processes exited and their temporary data was cleaned up. No Stripe request or real payment
  occurred. No host service was started, runtime qualified, production deployed or release published.

Evidence is in ignored `test-results/billing-refresh-before.log`, `billing-refresh-after.log`,
`billing-refresh-build.log`, `billing-browser.json` and `billing-checking-desktop.png`.

Stripe Checkout/Billing remains the proposed launch path already implemented locally. Launch prices, measured
job costs, merchant configuration and real hosted test-mode acceptance remain open. This regression repair
does not complete the three-phase product goal.
