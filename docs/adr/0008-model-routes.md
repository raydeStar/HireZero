# 0008: Model routes come from one catalog; one provider contract follows

Status: Accepted (Phase 2)

## Context

Model ids were hard-coded in six source files (company routing defaults, the OpenClaw gateway, the Developer's PR
planner and policy, the text-only recovery gateway), together with their display names. Changing a model meant a
code change in several places, and spend-relevant choices were invisible to the owner.

Twelve contracts call models or agent runtimes (`IModelGateway`, `ICompanyModel`, `ICompanyMeasuredModel`,
`ICodingRunner` and their helpers). They share no request shape, usage shape or accounting path; the legacy ones
return plain strings and record missing usage as zero.

## Decision

**Routes.** `config/model-routes.json` names every route by purpose (for example `company.text`,
`developer.escalation`) and every model's display name. It is embedded in Core and read through `ModelRoutes`.
A data root may override it with its own `model-routes.json`; that file is owner configuration and is never written
by HireZero. Source code names purposes, never model ids.

**Provider contract (Phase 2B).** The contracts are replaced by one provider abstraction with:

- one request: dispatch and attempt ids, route, instructions and context, input and output bounds, effort, mode
  (text, or coding with workspace and write and network permissions), isolation key;
- capability flags for exact input counting, measured (pinned runtime identity) and execution;
- one result with body, input, output and reasoning usage, receipt and stop reason, able to say "usage unknown"
  and to carry partial usage on failure;
- ownership of the call (start, find, cancel, confirm termination) so the scheduler can drain and accept late
  receipts;
- an authorization check immediately before dispatch (execution grant or measured job digest).

The company reservation and settlement path is the single source of truth for usage; the legacy budget ledger
becomes a projection of receipts.

## Consequences

- Phase 2A ships the route catalog and removes model ids from source; the provider contract lands with the
  Developer port in Phase 2B, because the legacy profile is its largest consumer.
- Route changes are reviewable configuration diffs. Existing tasks keep the route recorded when they were created.
