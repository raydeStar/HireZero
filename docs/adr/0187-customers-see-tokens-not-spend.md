# 0187 — Customers see tokens, not what the install spends on AI

Status: accepted, 2026-11-07. Owner request. Changes what [0177](0177-plain-spend-limits.md) and
[0181](0181-open-signup-and-durable-spend-caps.md) show to a free sign-up company; the limits themselves are unchanged.

## Context

0177 shows a paid model's per-job limit in dollars, and 0181 shows a free company its remaining allowance in dollars.
Both make sense to an owner who pays the provider. A free sign-up company on a hosted install pays nothing: the
dollars are what the install's operator spends on AI. The owner's words: customers don't need to hear how much is
spent on AI; they may hear the tokens used.

## Decision

- A **free sign-up company is a customer** of the install. Its workspace shows no AI spend in dollars anywhere:
  - no per-job spend-limit box; jobs keep the default limit, still checked on the server before anything is sent;
  - limits read "included in your plan", in place of "up to $0.05" or "included usage";
  - the free-allowance line says nothing while there is room, and says plainly when the plan or today's shared
    usage is used up, with no amounts;
  - where an amount was shown (the cockpit, Home, the brief, each reply), tokens used are shown, or nothing;
  - Budgets and Usage are not among its settings, like Billing and Integrations already; task-cost reconciliation and
    the cost sections of Progress are hidden.
- The switch is the existing `signupCompany` capability, read once by the workspace (`web/src/company/audience.ts`).
  An owner's own install, and every company that is not a free sign-up, is unchanged.
- The server's reservations, lifetime allowance and shared daily cap (0181) are unchanged; this decision is only about
  what a customer reads.

## Consequences

- A customer can't raise a job's limit. The default covers ordinary jobs; one too large for it is refused before
  anything is sent, as before.
- A rare refusal from the model provider still names its dollar limit; it needs a job far larger than ordinary work.
- The demo shows the caps from the operator's side (decision records and the deployment record), not on the
  customer's screen.
