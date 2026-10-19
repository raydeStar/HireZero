# 0031: Ads read contracts

Status: Accepted (Phase 4).

## Context

Phase 4's Marketing needs an ads platform: reads first, writes only with approval. Ads platforms (Google Ads,
Microsoft Advertising, Meta, LinkedIn) differ in their APIs and sign-in, but agree on what an owner needs to judge
performance. Spend is real money, so writes must never come from an action or a model.

## Decision

- **Neutral Read contracts.** `HireZero.Tools.Advertising` defines two tools:
  - `ads.status`: which account is connected;
  - `ads.performance`: impressions, clicks, spend, conversions (possibly fractional) and conversion value for a
    date range, as a total and broken down by campaign (with name and status) or by day.

  Money is in the account's currency, named by its ISO 4217 code. The request rules (range, rows, breakdown) live
  with the contract, so every provider refuses alike. The analytics contracts now carry their rules the same way.
- **Fictional first.** `HireZero.Integrations.Synthetic` gathers the deterministic stand-ins: the analytics one moved
  there from the Google integration, and a new ads one.
  - The ads stand-in is switched on with `HIREZERO_SYNTHETIC_ADS=1`.
  - It reports in XTS, the currency code reserved for testing, so fictional spend can never be read as real.
  - It plants situations worth reviewing: a campaign spending without converting, one whose cost per conversion
    worsens over a repeating four-month cycle, a paused campaign, and a spend spike every 41st day of the year.

  A real platform is a later integration behind the same contracts, chosen by the owner.
- **Marketing reads deterministically.**
  - The `ads-read` action compares the last 7, 28 or 90 complete days with the period before. It derives CTR, CPC,
    CPA and ROAS, and flags unusual days in spend and conversions with the same robust, day-kind-aware score as
    analytics ([ADR 0029](0029-marketing-analytics-reads.md)).
  - It lists campaigns needing attention by fixed rules. Each rule covers only campaigns with at least 5% of
    spend:
    - spend with no conversions;
    - CPA up by 30% or more;
    - ROAS below 1 where conversion value is tracked.
  - The measured `ads-review` skill writes the review from the approved evidence, and puts any changes as proposals.
- **Writes later, and only by approval.** Pausing a campaign or changing a budget or bid will be separate External
  tools. They will run only as an owner-approved publication of one exact call
  ([ADR 0016](0016-publishing-one-approved-call.md), [ADR 0017](0017-publishing-a-step-from-an-action.md)), and an
  uncertain outcome is never retried. No such tool exists yet.

## Consequences

Marketing can review ad performance today on fictional data, and on a real account as soon as one provider is
written. Adding a provider touches no role code. Spend changes stay impossible until the approval path is built for
them.
