# Company-owned model connections and fresh review

The all-three-phase goal remains active. This increment advances P2.6 and company-isolation/approval accounting;
it does not establish live provider access or complete a product phase.

## Implemented

- Owner-only model settings for a company-owned OpenAI, Anthropic or OpenRouter account, with explicit consent,
  an operator-reviewed priced model, encrypted company-specific persistence and no credential echo.
- Per-company model composition with no host-secret fallback. Saving does not call a provider. Unknown save
  responses require reading current state, and stale versions cannot replace another edit silently.
- Changed model authority expires unsent work and allows explicit fresh preparation. Returning to the host
  cannot resurrect an old approval. In-flight changes withhold results and retain uncertain usage.
- Direct-provider funding attribution on durable receipts and as a subset of usage totals. Existing monthly
  and subscription limits remain enforced, while developer execution keeps its separate funding source.
- Cleared stale context previews and consent when a replacement job appears. Fixed inherited checkbox alignment
  on narrow screens and separated the full wrapping model ID from the short model picker label.

Decision: [ADR 0085](../adr/0085-company-owned-model-connections.md).

## Verification

- **59 unique backend facts passed** across the new model connection/HTTP tests and existing API model, monthly
  usage, recurring schedule and business drafting suites. Twelve new facts cover encrypted reload, copied
  ciphertext rejection, company isolation, exact key selection, refusal without credential fallback, consent,
  version and price checks, replacement/removal authority, fresh review, retained funding, monthly/subscription
  enforcement, uncertain provider responses, in-flight changes, owner-only endpoints and CSRF.
- The initial selected run passed 46 of 58 tests. Eleven new tests hit sandbox restrictions on atomic temporary
  file moves, and one existing HTTP test hit restricted loopback access. Those twelve passed with the required
  local permissions, along with the new HTTP test. One intermediate command combined incompatible xUnit class
  and method filters and selected zero tests; it is excluded from the count.
- **Three Chrome journeys passed** against disposable loopback fixtures: new company-model settings and
  execution, existing conversation replies through the wrapped native fixture, and existing private-connection
  isolation. The new journey verifies two companies, zero provider calls while configuring, a lost save response
  resolved by reading state, old approval rejection, fresh in-place context/consent, one explicit fictional API
  call, retained usage attribution, removal/reload and a 390px layout without horizontal overflow.
- The first browser run caught vertically centered mobile consent inherited from shared fieldset styles. The
  corrected scoped rule passed the rerun. Desktop/mobile screenshots were inspected.
- Frontend typecheck/build, Core/Host/test-project builds, the existing company UI recovery checks, and formatting
  of the thirteen affected C# files passed. The existing main-bundle size warning remains.

Compact logs, screenshots and the JSON journey receipt are in ignored `test-results/byok-*` and
`test-results/company-model-*`. Test providers and keys are fictional. There were no live API requests, payments,
GPU work, container launches, commits, publications or deployments. Fixture processes exited and removed their
own temporary profiles/data. Free disk space remained above 314 GiB.

Production model availability/prices and live account acceptance remain unverified. Launch commercial terms,
real hosted Stripe acceptance, full operating-cost accounting and the remaining three-phase requirements stay
in the delivery ledger. Local key removal does not revoke a provider key or erase backup copies.
