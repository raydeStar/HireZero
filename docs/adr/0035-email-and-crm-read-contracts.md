# 0035: Email and CRM read contracts

Status: Accepted (Phase 4).

## Context

The last piece of Phase 4's Marketing is email and CRM. These systems hold the most personal data a business has:
subscribers, contacts and deals with named people. Marketing needs their performance, not their people.

## Decision

- **Aggregates only, by contract.** Two neutral contract projects:
  - `HireZero.Tools.Email`: `email.status` and `email.campaigns`. Each sent campaign's recipients, deliveries,
    unique opens and clicks, unsubscribes, bounces and complaints, with totals.
  - `HireZero.Tools.Crm`: `crm.status` and `crm.pipeline`:
    - open deals and value by stage, as a snapshot;
    - new contacts and deals;
    - deals won and lost, with value, and the average days to close;
    - results by lead source.

  The only text a record carries is an id, a name, a source or a currency, and a test enforces this, so no contact,
  address or deal record can cross the contract. Each contract carries the request rules every provider applies.
- **Fictional first.** `HIREZERO_SYNTHETIC_EMAIL=1` and `HIREZERO_SYNTHETIC_CRM=1` switch on deterministic
  stand-ins in `HireZero.Integrations.Synthetic`, with money in XTS. Planted situations:
  - a newsletter every Tuesday;
  - an imported list with high bounces every sixth week;
  - a complaint spike every ninth week;
  - an event source that brings contacts but no deals;
  - a win rate that sags over a repeating cycle.

  Real providers (Mailchimp, Brevo, HubSpot and others) come later, behind the same contracts.
- **Marketing reads deterministically.**
  - `email-read` compares the last 28 or 90 complete days with the period before. It gives delivery, click,
    click-to-open, unsubscribe, bounce and complaint rates. Opens are labelled soft, because mail privacy features
    inflate them.
  - It flags each campaign above fixed limits:
    - bounces above 2% of recipients;
    - complaints at or above 0.1% of delivered, below mailbox providers' 0.3% filtering line;
    - unsubscribes above 0.5%.
  - `pipeline-read` gives the stage snapshot, new contacts and deals, won and lost, win rate and days to close, and
    results by source.
  - It flags:
    - a source with at least 10% of new contacts and no deals;
    - a win rate down by a fifth or more (on at least ten closed deals);
    - won value down by a fifth or more.
  - The measured `lifecycle-review` skill writes the review from approved evidence and only proposes.
- **No writes.** Sending email, changing lists and editing the CRM are not possible. Any future write would follow
  the approved-change pattern of [ADR 0033](0033-approved-ad-changes.md).

## Consequences

Marketing now covers its whole Phase 4 list on fictional data: analytics, A/B tests, ads (with approved changes),
email and CRM. Because the contracts carry no personal data, connecting a real email or CRM provider adds no new
privacy surface to Marketing or its models. The limits are fixed rules of thumb, which owners may want to tune later.
