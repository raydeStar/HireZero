# 0085: Company-owned model connections

Date: 2026-10-26

Status: Accepted; fictional backend and desktop/mobile verification. Live provider acceptance remains separate.

## Context

Model requests used one host-wide connection. A company's integration credentials did not select the model
provider, and usage records did not distinguish an owner-funded API account from host-funded work. This left
the bring-your-own-key part of the context-first proposal unimplemented.

## Decision

- An owner may explicitly configure an OpenAI, Anthropic or OpenRouter key for the current company's reviewed
  text work. Only models in the operator-reviewed price catalog are offered. Vendor keys require matching
  provider routes; OpenRouter uses the selected provider/model route. Keys never authorize automatic calls.
- The host composes one model connection per company. Core receives routing, authority and funding callbacks;
  provider HTTP remains in the existing integration. Company keys never fall back to ambient host credentials.
  Code execution keeps its separately configured runtime and funding source.
- Save the key in an atomic encrypted file under the company's hashed data directory. ASP.NET Data Protection
  uses a company-specific purpose. Status returns provider, route, version and whether a key is stored, never
  the key. Unreadable ciphertext disables the connection until explicit replacement or return to the host.
- Saving or removing a connection performs no provider request. The UI distinguishes stored configuration
  from verified provider access, clears the input after a save attempt and never persists the key in browser
  storage. A lost save response requires a read of current state before another explicit change; it never
  automatically resubmits a secret. Expected-version checks reject stale edits.
- Every changed connection has new authority, including a return to the original host model. Old model
  reviews cannot become valid again. Unsent reviews expire without usage, release the task for explicit fresh
  preparation and clear its former text route. Running work remains fenced by its original authority; unknown
  usage and provider holds survive key changes. No canceled or uncertain call is retried automatically.
- Monthly safety limits and purchased subscription allowances still apply to company-funded work. This
  implementation grants no discount, free tier or billing exemption. Receipts capture funding source,
  provider and connection revision at admission. Historical attribution survives key replacement/removal.
  Usage totals include company-funded costs and identify that subset as direct provider charges; they are
  not added twice. Developer jobs retain a separate host/runtime label.
- Existing unconfigured companies preserve their original runtime identity. Optional funding fields keep old
  receipt JSON readable, with the UI labeling an unrecorded funding source honestly. No database migration,
  runtime pin change or built-in production API price is introduced.

Production API model availability, account permissions, current prices and provider access need operator review
and separately authorized acceptance. Full operating-cost accounting, launch commercial terms and real hosted
payment acceptance remain open requirements. Removing a locally stored key does not revoke it at the provider
or erase copies in backups.

Verification: [company model connection journey](../history/2026-10-26-company-model-connections.md).
