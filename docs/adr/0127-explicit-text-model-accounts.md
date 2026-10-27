# 0127: Keep the host connection beside an explicitly selected API model

Status: Accepted. Extends 0036 and company-owned model connections.

## Context

Connecting an API account previously replaced the workspace's text provider. A person intending to use a Codex
subscription for routine work and OpenRouter for another model could therefore route subscription work through
an API account. A workspace-wide allowance label also could not describe two different account types honestly.

## Decision

Owners may retain a measured non-API host connection as the default and add a separate company-owned API model.
Existing API-only configuration remains supported. Combined mode requires distinct host and API route identifiers;
the same route cannot ambiguously refer to both accounts. The existing company-bound encrypted key storage applies.

Employee assignments and replies can explicitly choose a configured text model before preparation. Recurring text
work retains that selection in its separately approved schedule. Unavailable selections require a fresh choice;
they never silently become the default. Changing a connection invalidates unsent reviews and schedule authority.

Each prepared job records provider and allowance terms in its reviewed digest. Dispatch verifies the route and
terms against the current connection, then invokes exactly one adapter. Native host jobs retain credit semantics;
API jobs retain US-cent preflight/accounting semantics. There is no failure fallback or automatic retry. Combined
mode does not enable automatic unreviewed model work. Repository execution keeps its separate runtime authority.

Usage admission records the selected account and route. An API price is never borrowed for a native host job.
Completed receipts display their recorded terms even if the workspace's default later changes. Existing records
without terms remain readable; pending legacy authority may require a fresh review after this change.

The settings page distinguishes adding an API choice from replacing the default. The task form clears an entered
allowance when its model selection changes, then the exact review shows the model, account and units.

## Verification and limits

Focused fictional provider and store tests cover both dispatch paths, encrypted persistence, stale approvals,
separate usage funding, invalid provider terms, no fallback and preserving a recurring route until removal. A real
browser walkthrough against compiled fictional providers covers setup, choice, cents labeling, review, one dispatch,
receipt and refresh. These checks do not authorize a real request or establish real API access. Production account
selection, installation delivery and real output quality remain separate acceptance work.
