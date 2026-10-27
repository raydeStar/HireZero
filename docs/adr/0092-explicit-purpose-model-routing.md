# 0092: Explicit purpose-based model routing

Date: 2026-10-26

Status: Accepted; fictional verification is separate from measured model quality and cost acceptance.

## Context

The product proposal calls for economical models on routine extraction, classification and drafting. A single
text route cannot express an owner's choice to use a different model for planning or analysis. Selecting a more
expensive route automatically after failure would also change the authority and cost of an approved request.

## Decision

- Keep the routine text and coding routes. Add an optional analysis route, falling back to the routine route when
  absent. Do not infer quality or affordability from a model's name or price. Actual suitability needs measurement.
- A role may declare the `analysis` model profile only for measured skills. Core reads that declaration without
  knowing role identities. CEO priorities/delegation and Developer planning/technical reviews opt in. Ordinary
  conversation, context extraction and marketing drafts retain the routine route. Owner memory checks use analysis.
- The host may set `HIREZERO_COMPANY_ANALYSIS_MODEL`. A company API connection may independently select a compatible
  analysis model from the same reviewed price catalog and provider. An omitted company choice uses its routine
  model, never inherits the host analysis choice or falls back to host credentials.
- Re-resolve queued work at preparation. Bind the actual route into the exact packet, digest and usage receipt.
  Changing routing expires old unsent approvals. An analysis-profile task with an analysis route configured is
  excluded from generic automatic claiming; the owner prepares and authorizes each request.
- Send the reviewed route through both API and native adapters. Preserve all existing request counts, token/time
  controls, price admission, monthly allowances and tool permissions. No larger limits follow from model choice.
- A failed or uncertain request never triggers a model substitution or retry. Preserve unresolved usage holds and
  require the existing recovery process. Returning to the routine model requires a fresh review.
- Leave the optional control collapsed by default, explain which work uses it, display prices and reset consent
  after changes. Saving a connection makes no provider request. Old serialized manifests/routes omit the new
  nullable field, preserving their fingerprints when the option is unused.

## Consequences

Owners can choose a cheaper routine route without forcing all analytical work onto it. This is explicit routing,
not a claim that the chosen route is the cheapest suitable model. Real quality/cost comparisons, production keys,
live payments and customer acceptance remain separate. No runtime pin, model catalog price or default changes.
