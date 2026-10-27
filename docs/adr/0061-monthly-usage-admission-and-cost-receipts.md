# 0061: Monthly usage admission and cost receipts

Status: Accepted. Adds periodic safety controls and durable metering beneath future commercial billing.

## Decision

Migration 19 adds a company-scoped monthly policy and usage-entry ledger. Every newly authorized employee,
recurring, developer and pre-hire business-profile AI job enters the ledger in the same database transaction
as its ordinary token reservation. Preparing a packet consumes no monthly allowance. Admitting a job consumes
one job slot even if it later fails, is canceled or is confirmed unsent; this meter counts admitted jobs, not
successful results, individual upstream retries or completed invoices.

The calendar period is UTC and is captured once with the dispatch-admission timestamp. Each company starts
with safety limits of 100 jobs and 1,000,000 planning tokens per month. An owner can set 0–10,000 jobs and
0–1,000,000,000 tokens, or pause new AI jobs. Exact policy versions prevent stale edits. Limits may be lowered
below recorded usage to prevent further admissions. They never erase usage or cancel work already authorized.
Existing task, employee, department, schedule and cumulative company limits continue to apply independently.

These are owner-controlled safety limits, **not subscription entitlements, purchased credits or a payment
authorization**. A future subscription entitlement must be enforced separately and cannot be raised by this
owner setting. Launch provider, plans, prices, subscriptions and checkout remain separate product work.

## Reservation and settlement

Admission checks the current month's settled tokens, outstanding reservations and carried reservations from
other months before inserting a new entry. Job limits also include earlier unresolved jobs. The transaction
serializes concurrent admissions, so two employees cannot both consume the final slot.

Successful settlement follows the existing reservation receipt exactly once. Late receipts stay attributed
to the original admission month. Unknown work retains its reservation across restart and month rollover;
observed overage raises the held amount above the original planning amount. Later incomplete observations
cannot reduce the largest observed token count. Existing provider-reconciliation holds still prevent replay.
Monthly accounting does not authorize an automatic retry or clear a provider hold.

Earlier reservations that predate this ledger have no reliable dispatch timestamp or historical price.
They appear in an explicit Earlier jobs group, with those fields unknown. They are not assigned a fabricated
month. Outstanding earlier reservations still constrain current admission. Reads derive this legacy view
from existing receipts without modifying them or treating unknown usage as zero.

## Cost evidence

Each new entry captures the configured model price at admission. Later price-table changes cannot rewrite
that job's estimate. Complete consistent provider-reported dollar cost takes precedence; otherwise the captured
price produces an estimate from input and output tokens. Reasoning is part of output usage and is not added
again. A job without a reported dollar cost or captured price remains explicitly unpriced.

The monthly view separates settled provider-reported cost, additional estimated cost, unpriced jobs and
provisional observations on unfinished jobs. Provisional costs do not appear as settled totals. These receipts
cover AI provider usage only. They are not subscription invoices and omit tool, hosting and storage costs.
Home, cockpit and cumulative totals use these ledger receipts for newly metered jobs without double-counting
the underlying attempt. Their labels identify costs and estimates. Earlier jobs without a ledger entry may
still use current-price estimates, which the budgets panel explains explicitly.

## User experience

The Usage page shows remaining allowance, the UTC reset date, carried holds, monthly totals, receipt details
and direct work links. The owner reviews limits before saving, can explicitly pause new work, and can recover
a lost HTTP response by its saved company-scoped operation ID. Policy refresh preserves unsaved edits and
requires a reload if another owner changed their version. The exact measured-packet review also shows the
current allowance; dispatch always rechecks it because concurrent work may have changed availability.

Per-period receipt pages return at most 50 entries with a stable identity cursor. Historic month selection
does not change the current policy or move late costs into a different period.

## Remaining

Payment-provider integration, commercial entitlements, invoice/reconciliation workflows, tenant API keys,
compute/tool/storage metering and real operational cost validation remain in the full product plan. No prices,
paid plan benefits or customer payments are inferred from these engineering defaults.
