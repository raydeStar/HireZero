# Purpose-based model routing — 2026-10-26

Implemented the explicit model-selection portion of X7 under
[ADR 0092](../adr/0092-explicit-purpose-model-routing.md). All three product phases remain in scope and incomplete.
These checks establish fictional behavior, not live model suitability, prices, quality or production acceptance.

## Delivered

- Routine conversation, context extraction and drafts keep the default text model. Roles declare an optional
  analysis purpose for measured CEO planning and Developer reviews; owner memory checks use that route too.
  Coding keeps its separate model. No default model, price catalog or runtime pin was changed.
- Host operators can configure an optional analysis route. Company owners can select a compatible priced model
  on their own API connection. Company defaults do not inherit the host analysis model, and another company
  cannot use its connection or approval. Saving the choice sends no model request.
- Queued tasks resolve the current choice when prepared; exact review, native/API dispatch and usage receipts
  agree. Changed settings expire unsent reviews. Analysis work with a separate route cannot enter the generic
  automatic claim path. Failures never trigger a fallback model or replay uncertain provider work.
- The optional selection is collapsed by default and explains affected work and listed prices. Consent resets
  on edits. Read-only recovery after a lost save response preserves the saved choice without replaying the write.
- Task review now displays the full selected route consistently with connection settings. Internal record IDs
  sit behind a disclosure, leaving the requested work and relevant facts easier to scan.

## Verification

- 17 Core manifest facts/theory cases pass, including invalid purpose/mode combinations, omitted-field
  serialization compatibility and default/coding routing.
- 75 existing Company regression cases pass across memory analysis, settings, delegation, first assignments,
  business/context drafting and monthly usage. The combined initial 92-case run had one additional new fixture
  failure because its native qualification allowance was missing/below the existing planning threshold.
  Correcting that fictional allowance to the existing constant made all 17 Company connection/routing cases pass.
- One Company HTTP test passes for CSRF, current owner authority and tenant isolation. In total, 110 distinct
  xUnit cases have passing evidence across these focused runs. A sandboxed connection-test attempt could not
  move temporary encrypted fixture files; the permitted fixture run passed with no production-data access.
- The company UI recovery contract test passes. TypeScript/Vite and Core, affected roles, Codex integration,
  Host, Company/Core tests and FakeCodex builds pass. Vite retains its existing bundle-size advisory.
- The real local HTTP/Chrome journey performs two approved fictional API requests: routine draft to
  `fictional-luna`, planning to `fictional-sol`. It checks exact approval, selected-price usage, invalidated prior
  reviews, lost-save recovery, two-company isolation, removal/reload, key secrecy and mobile overflow.
  Provider calls remain inside the fixture handler; there is no external model traffic or payment.
- Compact ignored evidence: `test-results/model-routing-xunit.log`, `model-routing-connection-xunit.log`,
  `model-routing-host-xunit.log`, `model-routing-browser.log`, `company-model-browser.json` and the
  `company-model-*.png` screenshots. Temporary browser profiles and databases are removed after owned processes exit.

## Remaining acceptance

Measure actual quality and full cost before claiming the cheapest suitable route or setting launch allowances.
Stripe commercial terms, production provider/payment acceptance, isolated runtime qualification and real pilot
outcomes remain separate work. No push, deployment, live model call, payment or runtime upgrade was performed.
