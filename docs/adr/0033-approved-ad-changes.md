# 0033: Approved ad changes

Status: Accepted (Phase 4).

## Context

Marketing can read ad performance ([ADR 0031](0031-ads-read-contracts.md), [ADR 0032](0032-google-ads-provider.md)).
The next step is acting on it: pausing a campaign that wastes money, or moving a budget. These changes spend or stop
spending real money. They must happen only when the owner approves the exact change, never twice, and never by a
model.

## Decision

- **Two more neutral tools.**
  - `ads.campaign` (Read) returns one campaign's live status and daily budget, and whether that budget is shared.
  - `ads.change` (External) pauses one campaign or sets its daily budget. The request carries the state it was
    approved against.

  The contract's rules refuse anything else, such as pausing a campaign that is not enabled, or a budget that is not
  positive or does not change.
- **A publishing action, like a pull-request step** ([ADR 0017](0017-publishing-a-step-from-an-action.md)). Marketing's
  `ads-change` action reads the campaign live and names one exact change, or says why none is allowed:
  - pause only an enabled campaign;
  - change only a budget the campaign does not share with others, since changing it would silently change them
    too;
  - move a budget by at most half of today's in one step, so a typo cannot multiply spend. Larger moves take
    several approvals.
  - Amounts are rounded to whole cents, half away from zero.

  The owner approves the reading, then the publication of that exact call, whose digest the approval covers.
- **Once, after a live re-check.**
  - Publishing reads the campaign again, and refuses unless it still yields the approved change.
  - If changes are off, it refuses before claiming. Either refusal leaves the publication approved, with nothing
    sent.
  - The call runs once with the publication's authority, and is consumed before it runs.
  - An unanswered or failed call is unknown and never retried. Reconciling only reads the campaign, and confirms
    the change when the campaign shows it.
- **A separate identity for changes.**
  - Google Ads reads keep the read-only user. Changes sign in as a second user who may edit, with the opt-in secret
    `HIREZERO_GOOGLE_ADS_CHANGE_REFRESH_TOKEN`.
  - Without it, `ads.change` is not registered at all, and the integration status says changes are off.
  - Each change is one `campaigns:mutate` (status `PAUSED`) or `campaignBudgets:mutate` (`amount_micros`) call.
- **The synthetic stand-in plays along.** `HIREZERO_SYNTHETIC_ADS=1` applies approved changes to its fictional
  campaigns in memory, and it has a shared budget to show that refusal. The whole flow can be tried without an ads
  account.
- **Tested end to end without an ads account.** The real store, registry and publication verifier run against fake
  ads tools: published once, re-check refusals, changes off, unknown outcomes reconciled only by reading. A fake
  handler checks Google Ads' sign-in, mutate URLs and bodies. Nothing calls Google Ads.

## Consequences

The owner can act on an ads review in two approvals per change, each naming the exact campaign, amounts and current
state. Bids, keywords, ads and new campaigns are not covered. Each would be its own change kind with its own rules.
The editing user's token is the most powerful secret Marketing holds; leaving it unset keeps Google Ads read-only.
