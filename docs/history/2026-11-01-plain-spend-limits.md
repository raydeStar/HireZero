# Spend limits in the owner's terms — 2026-11-01

Using the new + menu in the chat, the owner said "credit allowance feels weird and i dont understand it" and asked
whether to remove it. Both meanings of the field were confusing:
- For Luna through Codex, the company's default, it was "Credit allowance: 1". The unit is not shown anywhere, and
  nothing enforces it.
- For the paid OpenRouter model it was a spend cap in US cents. That cap is real: a request whose worst case could
  cost more is refused before it is sent.

[ADR 0177](../adr/0177-plain-spend-limits.md) records the change:
- **Included usage.** No allowance is asked for. The planning default of 1 is still sent and sealed with the review.
  The terms now say "included usage" and that it has no per-job limit.
- **Paid models.** The cap shows only when a paid model is chosen. It is typed and shown in dollars (default $0.05)
  and still stored in cents.
- **Everywhere it appeared.** One component replaces the field in:
  - the chat's + menu;
  - the reply and job review card;
  - Home's first draft and the first assignment;
  - context from sources, memory checks and business drafts;
  - schedules;
  - the goal ladder and the morning shift.

Server checks, budgets and stored values are unchanged.

## Checks

- All 55 frontend unit tests pass. The review-card test now loads the real allowance module. It checks that included
  usage reads "There is no per-job limit on it" and that the card never mentions credits.
- These headless Chrome journeys pass against fake providers, with no live model calls:
  - conversation replies, which now also check that the + menu asks for no allowance with included usage;
  - employee reviews, the manual measured queue and task handoff;
  - memory checks, goal start and the goal ladder;
  - the morning shift and delegation.
- **Tests changed with the UI.** Steps that typed into the removed field were dropped. Expected wording was updated.
  The paid-model test now types `1.00` dollars where it typed `100` cents.
- **Already failing.** Four journeys fail at the same check with and without this change, against the previous commit
  (3032a6b):
  - business drafting: "Approved brief supplies role setup without another blank questionnaire";
  - company model: a review that now loads by itself;
  - task cost: the cost panel;
  - schedules: "Split chat remains an explicit choice".

  They are left for a separate fix.
