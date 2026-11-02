# 0177 — Spend limits in the owner's terms

Status: accepted, 2026-11-01. Changes how [0036](0036-api-model-provider.md) and [0058](0058-bounded-recurring-work.md)
present the allowance. What the server checks and stores is unchanged.

The owner did not understand "Credit allowance: 1" in the chat and asked whether to remove it. For included usage,
such as a ChatGPT sign-in through Codex, the number is in no unit an owner can see, and nothing enforces it. The code
itself calls it "not a provider-enforced cap". Asking for it made every reply look as though it had a price the
owner had to set. For a paid API model the allowance is real: the adapter refuses a request whose worst case could
cost more.

- **Included usage.** The UI no longer asks for an allowance and sends the existing planning default of 1. It is
  still recorded in the sealed review and its digest, as before. Where the terms are shown, they say "included usage"
  and that it has no per-job limit.
- **Paid models.** The cap stays the owner's to set. It is entered and shown in dollars (default $0.05) and sent and
  stored in US cents, so existing jobs, schedules and receipts keep their values. It appears only when a paid model
  is chosen.
- **One component.** `web/src/company/allowance.tsx` holds the field and the wording. The places that use it:
  - the chat;
  - the review card;
  - Home's first draft and the first assignment;
  - context from sources, memory checks and business drafts;
  - schedules;
  - the goal ladder and the morning shift.
- **Not changed.** Server validation of `creditAllowance` (positive, at most 1,000) and budget reservations are
  unchanged. Decisions always run on a paid model with an explicitly soft cents target, so they keep their own field.
